using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Desktop;

public partial class MainWindow
{
    private readonly SemaphoreSlim performanceRefreshGate = new(1, 1);

    private static byte? SelectedShift(ComboBox choice) =>
        choice.SelectedItem is ComboBoxItem item && byte.TryParse(item.Tag?.ToString(), out var value) ? value : null;

    private static void SelectShift(ComboBox choice, byte? value)
    {
        choice.SelectedIndex = -1;
        foreach (ComboBoxItem item in choice.Items)
            if ((byte.TryParse(item.Tag?.ToString(), out var current) ? (byte?)current : null) == value)
            { choice.SelectedItem = item; break; }
    }

    private async Task RefreshPerformanceAsync()
    {
        await performanceRefreshGate.WaitAsync(lifetime.Token);
        try { await CapturePerformanceCoreAsync(); }
        finally { performanceRefreshGate.Release(); }
    }

    private async Task CapturePerformanceCoreAsync()
    {
        try
        {
            performanceSnapshot = await CapturePerformanceAsync();
            SelectShift(DevicePerformanceChoice, performanceSnapshot.RawShiftValue);
            PerformanceStatusText.Text = performanceSnapshot.ReviewedShift is null ? "设备档位未识别" : "";
            ApplyPerformanceButton.IsEnabled = performanceSnapshot.ReviewedShift is not null && activeCurveId is null && !profileActionBusy && !fanActionBusy;
        }
        catch (Exception ex)
        { performanceSnapshot = null; ApplyPerformanceButton.IsEnabled = false; PerformanceStatusText.Text = "不可用"; ActionDiagnosticText.Text = ex.Message; }
    }

    private async void ApplyPerformanceClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || profileActionBusy || fanActionBusy || activeCurveId is not null || SelectedShift(DevicePerformanceChoice) is not byte target) return;
        profileActionBusy = true; ApplyPerformanceButton.IsEnabled = false;
        var acquired = false;
        try
        {
            await performanceRefreshGate.WaitAsync(lifetime.Token); acquired = true;
            var id = Guid.NewGuid();
            FinishTuningRecording();
            var directory = RequestDirectory("performance", id.ToString("N"));
            WriteAtomic(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(new { RequestId = id, RawRequestedByte = target, TrialSeconds = 0 }, JsonOptions));
            PerformanceStatusText.Text = "应用中…";
            using var worker = StartControlWorker("performance-worker", id.ToString("N"));
            var exit = worker.WaitForExitAsync(lifetime.Token);
            if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(60), lifetime.Token)) != exit)
                PerformanceStatusText.Text = "等待设备回读…";
            await exit;
            using var result = ReadControlJson(Path.Combine(directory, "result.json"));
            var root = result.RootElement;
            if (worker.ExitCode != 0 || root.GetProperty("IsSimulated").GetBoolean() || root.GetProperty("RequestId").GetGuid() != id ||
                !root.GetProperty("ReadBackVerified").GetBoolean() || root.GetProperty("FinalRawByte").GetByte() != target)
                throw new InvalidDataException(root.GetProperty("Reason").GetString() ?? "性能档回读失败。");
            await CapturePerformanceCoreAsync();
            if (performanceSnapshot?.RawShiftValue != target) throw new IOException("设备性能档已被其他程序更改。");
            PerformanceStatusText.Text = "";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { PerformanceStatusText.Text = "已取消"; }
        catch (Exception ex) { PerformanceStatusText.Text = "未应用"; ActionDiagnosticText.Text = ex.Message; }
        finally
        {
            if (acquired) performanceRefreshGate.Release();
            profileActionBusy = false; ApplyPerformanceButton.IsEnabled = performanceSnapshot?.ReviewedShift is not null && activeCurveId is null;
        }
    }

    private void SetBalanceIndicator(int? position)
    {
        balancePosition = position;
        if (BalanceSlider is null) return;
        settingBalance = true;
        try
        {
            BalanceSlider.IsEnabled = deviceCurves is not null;
            BalanceSlider.Value = position ?? 50;
            BalanceSlider.Opacity = position is null ? .65 : 1;
            BalanceSlider.ToolTip = deviceCurves is null ? "读取设备曲线后调整方案倾向" : position is null ? "自定义曲线；拖动以生成新的方案" : $"方案倾向 {position}";
        }
        finally { settingBalance = false; }
    }

    private void BalanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settingBalance || fillingCurve || deviceCurves is null || ProfileName is null) return;
        try
        {
            var name = string.IsNullOrWhiteSpace(ProfileName.Text) ? "我的方案" : ProfileName.Text.Trim();
            var profile = ProfileBalance.Create(name, deviceCurves.Cpu, deviceCurves.Gpu, (int)e.NewValue);
            settingBalance = true;
            try { FillCurve(cpuPoints, profile.Cpu); FillCurve(gpuPoints, profile.Gpu); }
            finally { settingBalance = false; }
            SetBalanceIndicator(profile.BalancePosition); ProfileStatusText.Text = "";
        }
        catch (Exception ex) { ActionDiagnosticText.Text = ex.Message; }
    }

    private async void ApplyProfileClick(object sender, RoutedEventArgs e)
    {
        try { await ApplyProfileAsync(ReadProfile()); }
        catch (Exception ex) { ProfileStatusText.Text = "未应用"; ActionDiagnosticText.Text = ex.Message; }
    }

    private async Task ApplyProfileAsync(ConsoleProfile target)
    {
        if (smokeOutput is not null || profileActionBusy || fanActionBusy) return;
        profileActionBusy = true; ApplyProfileButton.IsEnabled = false;
        var launched = false;
        try
        {
            if (activeCurveId is not null) await StopCurveSessionAsync();
            FinishTuningRecording();
            var baseline = FanCurveControlBaseline.FromSaved(await CaptureSavedCurvesAsync());
            FanCurveControlPolicy.Require(baseline, target.Cpu, target.Gpu, false, DateTimeOffset.UtcNow);
            var errors = (settings with { Profiles = [target] }).Validate();
            if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
            var id = Guid.NewGuid().ToString("N");
            var directory = RequestDirectory("curve", id);
            using var parent = Process.GetCurrentProcess();
            WriteAtomic(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(new
            { Target = target, ExpectedBaseline = baseline, ParentPid = parent.Id, ParentStartUtc = new DateTimeOffset(parent.StartTime.ToUniversalTime()), RequestId = id, IsTrial = false, TrialSeconds = 5 }, JsonOptions));
            activeCurveId = id;
            WriteAtomic(Path.Combine(directory, "heartbeat"), DateTimeOffset.UtcNow.ToString("O"));
            curveHeartbeat.Start();
            activeCurveWorker = StartControlWorker("curve-worker", id);
            var worker = activeCurveWorker;
            launched = true; SetProfileRunning(true);
            ProfileStatusText.Text = "应用中…";
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (File.Exists(Path.Combine(directory, "result.json")))
                {
                    using var result = ReadControlJson(Path.Combine(directory, "result.json"));
                    throw new IOException(result.RootElement.GetProperty("reason").GetString() ?? "方案未运行。");
                }
                if (File.Exists(Path.Combine(directory, "status.json")))
                {
                    using var status = ReadControlJson(Path.Combine(directory, "status.json"));
                    if (status.RootElement.GetProperty("phase").GetString() == "Active")
                    {
                        var live = await CaptureSavedCurvesAsync();
                        if (live.IsSimulated || live.Mode != LegacyFanCurveBinding.ReviewedCustomMode || !live.Cpu.SequenceEqual(target.Cpu) || !live.Gpu.SequenceEqual(target.Gpu))
                            throw new IOException("设备曲线回读不匹配。");
                        if (target.PerformanceShift is byte shift)
                        {
                            var perf = await CapturePerformanceAsync();
                            if (perf.RawShiftValue != shift) throw new IOException("方案性能档回读不匹配。");
                        }
                        activeProfileName = target.Name;
                        activeProfile = target;
                        unconfirmedRecoveryId = null;
                        ProfileStatusText.Text = "运行中"; SetProfileRunning(true); RefreshTrayProfiles(); return;
                    }
                }
                if (worker.HasExited) throw new IOException("方案进程已退出，详见诊断。");
                await Task.Delay(250, lifetime.Token);
            }
            throw new TimeoutException("方案启动超时，已请求恢复。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { RequestCurveStop(); ProfileStatusText.Text = "已取消"; }
        catch (Exception ex)
        { RequestCurveStop(); ProfileStatusText.Text = "未应用"; ActionDiagnosticText.Text = ex.Message; }
        finally
        {
            if (!launched) { activeCurveId = null; curveHeartbeat.Stop(); SetProfileRunning(false); }
            profileActionBusy = false; ApplyProfileButton.IsEnabled = true;
        }
    }

    private async void StopProfileClick(object sender, RoutedEventArgs e)
    {
        if (profileActionBusy) return;
        profileActionBusy = true; StopProfileButton.IsEnabled = false;
        try { await StopCurveSessionAsync(); }
        catch (Exception ex) { ProfileStatusText.Text = "恢复未确认"; ActionDiagnosticText.Text = ex.Message; }
        finally { profileActionBusy = false; StopProfileButton.IsEnabled = true; }
    }

    private async Task StopCurveSessionAsync()
    {
        if (activeCurveId is not string id) return;
        var directory = RequestDirectory("curve", id);
        if (activeCurveWorker?.HasExited == true && !File.Exists(Path.Combine(directory, "result.json")))
        { await RecoverCurveAsync(id); return; }
        FinishTuningRecording();
        RequestCurveStop(); ProfileStatusText.Text = "恢复中…";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (!File.Exists(Path.Combine(directory, "result.json")) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(250, lifetime.Token);
        await VerifyCurveRestoredAsync(directory, id);
        activeCurveId = null; activeProfileName = null; activeProfile = null; curveHeartbeat.Stop(); SetProfileRunning(false); RefreshTrayProfiles(); ProfileStatusText.Text = "";
        activeCurveWorker?.Dispose(); activeCurveWorker = null; RefreshPendingRecovery();
        await RefreshPerformanceAsync();
    }

    private void SetProfileRunning(bool running)
    {
        StopProfileButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ApplyPerformanceButton.IsEnabled = !running && performanceSnapshot?.ReviewedShift is not null;
        UpdateDraftBadge();
    }

    private void RequestCurveStop()
    {
        if (activeCurveId is not string id) return;
        try { WriteAtomic(Path.Combine(RequestDirectory("curve", id), "stop"), DateTimeOffset.UtcNow.ToString("O")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private bool verifyingCurveResult;

    private async void UpdateCurveHeartbeat()
    {
        if (activeCurveId is not string id) return;
        try
        {
            var directory = RequestDirectory("curve", id);
            if (activeCurveWorker?.HasExited == true && !File.Exists(Path.Combine(directory, "result.json")))
            {
                curveHeartbeat.Stop(); RefreshPendingRecovery();
                ProfileStatusText.Text = "需要恢复"; return;
            }
            if (File.Exists(Path.Combine(directory, "result.json")))
            {
                using var result = ReadControlJson(Path.Combine(directory, "result.json"));
                var root = result.RootElement;
                if (profileActionBusy || verifyingCurveResult) return;
                if (root.GetProperty("outcome").GetString() == "Rejected" && !root.GetProperty("hardwareWriteAttempted").GetBoolean() &&
                    !root.GetProperty("performanceWriteAttempted").GetBoolean())
                {
                    activeCurveId = null; curveHeartbeat.Stop(); SetProfileRunning(false);
                    activeCurveWorker?.Dispose(); activeCurveWorker = null;
                    ProfileStatusText.Text = "未应用"; ActionDiagnosticText.Text = root.GetProperty("reason").GetString() ?? "";
                    RefreshTrayProfiles(); RefreshPendingRecovery(); return;
                }
                verifyingCurveResult = true;
                var verified = false;
                try { await VerifyCurveRestoredAsync(directory, id); verified = true; }
                catch (Exception ex) { ActionDiagnosticText.Text = ex.Message; }
                finally { verifyingCurveResult = false; }
                unconfirmedRecoveryId = verified ? null : id;
                FinishTuningRecording();
                activeCurveId = null; activeProfileName = null; activeProfile = null; curveHeartbeat.Stop(); SetProfileRunning(false); RefreshTrayProfiles();
                activeCurveWorker?.Dispose(); activeCurveWorker = null; RefreshPendingRecovery();
                ProfileStatusText.Text = verified ? "已恢复自动" : "恢复未确认";
                if (verified) ActionDiagnosticText.Text = root.GetProperty("reason").GetString() ?? "";
                _ = RefreshPerformanceAsync();
                return;
            }
            WriteAtomic(Path.Combine(directory, "heartbeat"), DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        { RequestCurveStop(); ActionDiagnosticText.Text = ex.Message; }
    }

    private async Task VerifyCurveRestoredAsync(string directory, string id)
    {
        using var result = ReadControlJson(Path.Combine(directory, "result.json"));
        var root = result.RootElement;
        if (root.GetProperty("requestId").GetString() != id || root.GetProperty("isSimulated").GetBoolean() ||
            !root.GetProperty("recoveryVerified").GetBoolean() || root.GetProperty("outcome").GetString() is not ("Restored" or "ThermalFallback") ||
            root.GetProperty("finalMode").GetByte() != LegacyFanCurveBinding.ReviewedAutomaticMode)
            throw new IOException(root.GetProperty("reason").GetString() ?? "恢复未确认。");
        using var journal = ReadControlJson(Path.Combine(directory, "journal.json"));
        if (journal.RootElement.GetProperty("requestId").GetString() != id) throw new IOException("恢复记录不匹配。");
        var baseline = journal.RootElement.GetProperty("baseline").Deserialize<FanCurveControlBaseline>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new IOException("原始曲线记录缺失。");
        var live = await CaptureSavedCurvesAsync();
        if (baseline.IsSimulated || live.IsSimulated || live.Mode != LegacyFanCurveBinding.ReviewedAutomaticMode ||
            !live.Cpu.SequenceEqual(baseline.Cpu) || !live.Gpu.SequenceEqual(baseline.Gpu))
            throw new IOException("独立曲线回读与恢复记录不一致。");
        if (journal.RootElement.TryGetProperty("performanceMutation", out var mutation) && mutation.ValueKind == JsonValueKind.Object)
        {
            var original = mutation.GetProperty("originalShift").GetByte();
            var performance = await CapturePerformanceAsync();
            if (performance.RawShiftValue != original || root.GetProperty("finalPerformanceShift").GetByte() != original)
                throw new IOException("独立性能档回读与恢复记录不一致。");
        }
    }

    private static string RequestDirectory(string kind, string id)
    {
        if (kind is not ("curve" or "performance" or "fan") || !Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("无效控制请求。");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control", kind + "-requests", id);
        Directory.CreateDirectory(path); return path;
    }

    private static Process StartControlWorker(string action, string id)
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "helper", "Gs66Control.Helper.exe");
        if (!File.Exists(helper)) throw new FileNotFoundException("控制组件缺失。", helper);
        var start = new ProcessStartInfo(helper)
        { Arguments = action + " " + id, UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(helper)! };
        return Process.Start(start) ?? throw new IOException("控制进程未启动。");
    }

    private static JsonDocument ReadControlJson(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) throw new IOException("控制结果缺失或过大。");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

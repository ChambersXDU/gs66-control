using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Gs66Control.Windows;

namespace Gs66Control.Desktop;

public partial class MainWindow
{
    private CpuPolicySnapshot? cpuPolicySnapshot;
    private bool cpuPolicyBusy;

    private async Task RefreshCpuPolicyAsync()
    {
        try
        {
            cpuPolicySnapshot = await Task.Run(() => new WindowsCpuPolicyBackend().Capture(), lifetime.Token);
            if (cpuPolicySnapshot.IsSimulated) throw new IOException("CPU 策略来源无效。");
            ShowCpuPolicy(); ApplyCpuPolicyButton.IsEnabled = !cpuPolicyBusy;
            ShowPersonalPolicy();
        }
        catch (Exception ex)
        { cpuPolicySnapshot = null; ApplyCpuPolicyButton.IsEnabled = false; CpuPolicyStatus.Text = "不可用"; ActionDiagnosticText.Text = ex.Message; ShowPersonalPolicy(); }
    }

    private void ShowCpuPolicy()
    {
        if (cpuPolicySnapshot is null || CpuPowerSourceChoice is null || CpuEnergyPreference is null) return;
        var ac = CpuPowerSourceChoice.SelectedIndex != 1;
        var values = ac ? cpuPolicySnapshot.Ac : cpuPolicySnapshot.Dc;
        CpuEnergyPreference.Value = cpuPolicySnapshot.EffectiveEnergyPreference(ac);
        CpuBoostChoice.SelectedIndex = values.BoostMode;
        CpuMaximumState.Text = values.MaximumStatePercent.ToString(CultureInfo.InvariantCulture);
        CpuMaximumFrequency.Text = values.MaximumFrequencyMhz == 0 ? "自动" : values.MaximumFrequencyMhz.ToString(CultureInfo.InvariantCulture);
        var overlay = ac ? cpuPolicySnapshot.AcOverlay : cpuPolicySnapshot.DcOverlay;
        CpuPolicyStatus.Text = overlay is null ? "当前没有电源滑块覆盖。" :
            $"基础能效 {values.EnergyPreference} · 滑块能效 {overlay.EnergyPreference}；应用时同步这两处。";
    }

    private void CpuPowerSourceChanged(object sender, SelectionChangedEventArgs e) => ShowCpuPolicy();
    private async void RefreshCpuPolicyClick(object sender, RoutedEventArgs e)
    { if (!cpuPolicyBusy) await RefreshPersonalTuningAsync(); }

    private async void ApplyCpuPolicyClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || cpuPolicySnapshot is null || cpuPolicyBusy || powerPlanActionBusy || profileActionBusy || fanActionBusy) return;
        try
        {
            var automaticFrequency = CpuMaximumFrequency.Text.Trim() == "自动";
            var frequency = 0;
            if (!int.TryParse(CpuMaximumState.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) ||
                (!automaticFrequency && !int.TryParse(CpuMaximumFrequency.Text, NumberStyles.None, CultureInfo.InvariantCulture, out frequency)))
                throw new InvalidDataException("请输入整数；频率可填“自动”。");
            var target = new CpuPolicyValues((int)Math.Round(CpuEnergyPreference.Value), CpuBoostChoice.SelectedIndex, frequency, maximum);
            await ApplyCpuTargetAsync(target, CpuPowerSourceChoice.SelectedIndex != 1, null);
            CpuPolicyStatus.Text = "已应用并回读；基础计划与当前电源滑块能效一致。";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { CpuPolicyStatus.Text = "已取消"; }
        catch (Exception ex) { CpuPolicyStatus.Text = "未确认"; ActionDiagnosticText.Text = ex.Message; }
    }

    private async Task<CpuPolicySnapshot> ApplyCpuTargetAsync(CpuPolicyValues target, bool ac, string? presetId, CpuPolicyChange? restore = null)
    {
        if (smokeOutput is not null || cpuPolicyBusy || powerPlanActionBusy || profileActionBusy || fanActionBusy)
            throw new IOException("另一个设备操作正在进行，请稍后重试。");
        target.ValidateTarget();
        cpuPolicyBusy = true; CpuPolicyFields.IsEnabled = false; ApplyCpuPolicyButton.IsEnabled = false;
        PersonalApplyButton.IsEnabled = PersonalUndoButton.IsEnabled = false;
        FinishTuningRecording();
        try
        {
            var expected = cpuPolicySnapshot;
            var before = await Task.Run(() => new WindowsCpuPolicyBackend().Capture(), lifetime.Token);
            if (expected is not null && !WindowsCpuPolicyBackend.SamePolicy(expected, before))
                throw new IOException("电源计划或滑块参数已改变，请先刷新后重试。");
            // Persist the original values before any write, even if saving the final undo record later fails.
            WriteAtomic(Path.Combine(settingsDirectory, "cpu-policy-pending.json"),
                JsonSerializer.Serialize(new { Before = before, Target = target, Ac = ac, PresetId = presetId, RestorePrevious = restore?.Before }, JsonOptions));
            CpuPolicySnapshot result;
            try
            {
                result = await Task.Run(() => restore is null
                    ? new WindowsCpuPolicyBackend().Apply(before, target, ac)
                    : new WindowsCpuPolicyBackend().Restore(restore.Before, restore.After, ac), lifetime.Token);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                result = await ApplyCpuPolicyElevatedAsync(before, target, ac, restore?.Before);
            }
            var readBack = await Task.Run(() => new WindowsCpuPolicyBackend().Capture(), lifetime.Token);
            if (!WindowsCpuPolicyBackend.SamePolicy(result, readBack)) throw new IOException("CPU 参数已被其他程序更改，未确认本次应用。");
            cpuPolicySnapshot = readBack;
            if (!WindowsCpuPolicyBackend.SamePolicy(before, readBack))
                WriteAtomic(CpuUndoPath, JsonSerializer.Serialize(new CpuPolicyChange(before, readBack, ac, presetId), JsonOptions));
            File.Delete(Path.Combine(settingsDirectory, "cpu-policy-pending.json"));
            var next = settings with { LastCpuPresetId = ac ? presetId : settings.LastCpuPresetId };
            store.Save(next); settings = next;
            ShowCpuPolicy(); ShowPersonalPolicy(); RefreshPersonalTray();
            return readBack;
        }
        finally
        {
            cpuPolicyBusy = false; CpuPolicyFields.IsEnabled = true;
            ApplyCpuPolicyButton.IsEnabled = cpuPolicySnapshot is not null;
            PersonalApplyButton.IsEnabled = cpuPolicySnapshot is not null;
            PersonalUndoButton.IsEnabled = File.Exists(CpuUndoPath);
        }
    }

    private async Task<CpuPolicySnapshot> ApplyCpuPolicyElevatedAsync(CpuPolicySnapshot before, CpuPolicyValues target, bool ac, CpuPolicySnapshot? restore)
    {
        var id = Guid.NewGuid(); var directory = RequestDirectory("cpu-policy", id.ToString("N"));
        using var parent = Process.GetCurrentProcess();
        var request = new CpuPolicyActionRequest(id, before, target, ac, restore, DateTimeOffset.UtcNow,
            parent.Id, new DateTimeOffset(parent.StartTime.ToUniversalTime()));
        WriteAtomic(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(request, JsonOptions));
        using var worker = StartControlWorker("cpu-policy-worker", id.ToString("N"));
        await worker.WaitForExitAsync(lifetime.Token);
        using var document = ReadControlJson(Path.Combine(directory, "result.json"));
        var result = document.RootElement.Deserialize<CpuPolicyActionResult>() ?? throw new IOException("CPU 控制结果缺失。");
        if (worker.ExitCode != 0 || result.RequestId != id || !result.Verified || result.After is null || result.After.IsSimulated)
            throw new IOException(result.Error ?? "CPU 策略回读失败。");
        return result.After;
    }
}

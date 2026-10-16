using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Desktop;

public partial class MainWindow
{
    private Process? fanReadWorker;
    private string? fanReadId;
    private bool fanReadStarting;
    private bool externalFanReader;
    private DateTimeOffset fanReadStartedUtc;
    private readonly DispatcherTimer fanReadHeartbeat = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim controlReadGate = new(1, 1);

    private async void EnableFanReadClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || fanReadStarting || IsFanReaderRunning) return;
        fanReadStarting = true; EnableFanReadButton.IsEnabled = false;
        try
        {
            FinishTuningRecording();
            var id = Guid.NewGuid().ToString("N");
            using var parent = Process.GetCurrentProcess();
            var directory = RequestDirectory("fan", id);
            var request = new FanTelemetryRequest(id, parent.Id, new DateTimeOffset(parent.StartTime.ToUniversalTime()));
            request.Validate();
            WriteAtomic(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(request));
            WriteAtomic(Path.Combine(directory, "heartbeat"), DateTimeOffset.UtcNow.ToString("O"));
            fanReadId = id;
            fanReadStartedUtc = DateTimeOffset.UtcNow;
            fanReadWorker = StartControlWorker("fan-reader", id);
            fanReadHeartbeat.Start();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(directory, "frame.json")))
            {
                if (!IsFanReaderRunning) throw new IOException("风扇读取进程已退出。");
                if (DateTimeOffset.UtcNow > deadline) throw new IOException("风扇读取尚未就绪。");
                await Task.Delay(100, lifetime.Token);
            }
            await RefreshAsync();
            await RefreshPerformanceAsync();
            deviceCurves = await CaptureSavedCurvesAsync(); SetBalanceIndicator(balancePosition);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { StopFanReader(); }
        catch (Exception ex) { StopFanReader(); ActionDiagnosticText.Text = ex.Message; }
        finally { fanReadStarting = false; EnableFanReadButton.IsEnabled = true; }
    }

    private void TickFanReader()
    {
        if (fanReadId is not string id || !IsFanReaderRunning)
        { StopFanReader(); return; }
        try
        {
            var directory = RequestDirectory("fan", id);
            var framePath = Path.Combine(directory, "frame.json");
            var lastFrame = File.Exists(framePath) ? new DateTimeOffset(File.GetLastWriteTimeUtc(framePath)) : fanReadStartedUtc;
            if (DateTimeOffset.UtcNow - lastFrame > TimeSpan.FromSeconds(15)) { StopFanReader(); return; }
            WriteAtomic(Path.Combine(directory, "heartbeat"), DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StopFanReader(); ActionDiagnosticText.Text = ex.Message; }
    }

    private void StopFanReader()
    {
        if (fanReadId is not null) FinishTuningRecording();
        fanReadHeartbeat.Stop();
        if (fanReadId is string id)
        {
            try { WriteAtomic(Path.Combine(RequestDirectory("fan", id), "stop"), "stop"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        fanReadId = null; externalFanReader = false; fanReadWorker?.Dispose(); fanReadWorker = null;
    }

    private TelemetrySnapshot ReadFanFrame(TelemetrySnapshot snapshot)
    {
        if (fanReadId is not string id || !IsFanReaderRunning) return snapshot;
        try
        {
            var path = Path.Combine(RequestDirectory("fan", id), "frame.json");
            if (!File.Exists(path)) return snapshot;
            if (new FileInfo(path).Length > 8192) throw new InvalidDataException("风扇读数文件过大。");
            var frame = JsonSerializer.Deserialize<FanTelemetryFrame>(File.ReadAllText(path)) ?? throw new InvalidDataException("风扇读数缺失。");
            frame.RequireFresh(id, DateTimeOffset.UtcNow);
            return snapshot with
            {
                CpuFanRpm = frame.CpuFanRpm, GpuFanRpm = frame.GpuFanRpm, FanSource = frame.Source,
                CpuTemperatureCelsius = frame.CpuTemperatureCelsius, GpuTemperatureCelsius = frame.GpuTemperatureCelsius,
                GpuPower = frame.GpuPower,
                FanReadRequiresElevation = false,
                Warnings = snapshot.Warnings.Where(w => !w.StartsWith("MSI fan telemetry unavailable:", StringComparison.Ordinal) &&
                    !w.StartsWith("CPU temperature unavailable:", StringComparison.Ordinal) && !w.StartsWith("GPU temperature unavailable:", StringComparison.Ordinal) &&
                    !w.StartsWith("CPU 温度当前不可用", StringComparison.Ordinal) && !w.StartsWith("GPU 温度当前不可用", StringComparison.Ordinal)).Concat(frame.Warnings).ToArray()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { ActionDiagnosticText.Text = ex.Message; return snapshot; }
    }

    private void RefreshFanReadButton()
    {
        if (EnableFanReadButton is null) return;
        EnableFanReadButton.Visibility = latest?.FanReadRequiresElevation == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<WindowsControlReadPacket> ReadControlsAsync()
    {
        await controlReadGate.WaitAsync(lifetime.Token);
        try
        {
            if (fanReadId is not string id || !IsFanReaderRunning) throw new IOException("请先用盾牌图标启用硬件读数。");
            var directory = RequestDirectory("fan", id);
            var nonce = Guid.NewGuid().ToString("N");
            WriteAtomic(Path.Combine(directory, "control-request.json"), JsonSerializer.Serialize(new WindowsControlReadRequest(nonce, DateTimeOffset.UtcNow)));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (!IsFanReaderRunning) throw new IOException("硬件读取进程已退出。");
                var path = Path.Combine(directory, "control-result.json");
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("硬件读取结果过大。");
                    var packet = JsonSerializer.Deserialize<WindowsControlReadPacket>(File.ReadAllText(path));
                    if (packet?.Nonce == nonce) { packet.RequireFresh(nonce, DateTimeOffset.UtcNow); return packet; }
                }
                await Task.Delay(100, lifetime.Token);
            }
            throw new IOException("硬件读取超时。");
        }
        finally { controlReadGate.Release(); }
    }

    private async Task<SavedFanCurves> CaptureSavedCurvesAsync() => IsFanReaderRunning
        ? (await ReadControlsAsync()).Curves! : await Task.Run(() => new WindowsSavedCurveProbe().Capture(lifetime.Token), lifetime.Token);

    private async Task<WindowsPerformanceSnapshot> CapturePerformanceAsync() => IsFanReaderRunning
        ? (await ReadControlsAsync()).Performance! : await Task.Run(() => new WindowsPerformanceBackend().CaptureCurrent(lifetime.Token), lifetime.Token);

    private async Task PrepareSmokeReaderAsync()
    {
        if (smokeOutput is null) return;
        var id = Guid.NewGuid().ToString("N");
        using var parent = Process.GetCurrentProcess();
        var request = new FanTelemetryRequest(id, parent.Id, new DateTimeOffset(parent.StartTime.ToUniversalTime()));
        var directory = RequestDirectory("fan", id);
        WriteAtomic(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(request));
        WriteAtomic(Path.Combine(directory, "heartbeat"), DateTimeOffset.UtcNow.ToString("O"));
        fanReadId = id;
        fanReadStartedUtc = DateTimeOffset.UtcNow;
        WriteAtomic(Path.Combine(smokeOutput, "sensor-session.json"), JsonSerializer.Serialize(request));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            WriteAtomic(Path.Combine(directory, "heartbeat"), DateTimeOffset.UtcNow.ToString("O"));
            var announcement = Path.Combine(directory, "reader.json");
            if (File.Exists(announcement) && File.Exists(Path.Combine(directory, "frame.json")))
            {
                if (new FileInfo(announcement).Length > 2048) throw new InvalidDataException("Reader announcement is too large.");
                using var document = JsonDocument.Parse(File.ReadAllText(announcement));
                var root = document.RootElement;
                if (root.GetProperty("RequestId").GetString() != id) throw new InvalidDataException("Reader session mismatch.");
                if (root.GetProperty("ReaderPid").GetInt32() <= 0) throw new InvalidDataException("Reader process identity missing.");
                externalFanReader = true; fanReadHeartbeat.Start();
                await RefreshAsync(); await RefreshPerformanceAsync(); return;
            }
            await Task.Delay(100, lifetime.Token);
        }
        throw new IOException("Read-only smoke requires a separately authorized sensor reader; no elevation was attempted by the UI.");
    }

    private bool IsFanReaderRunning
    {
        get
        {
            if (fanReadId is null) return false;
            if (externalFanReader) return true;
            if (fanReadWorker is null) return false;
            try { return !fanReadWorker.HasExited; }
            catch (Win32Exception) { return true; }
        }
    }
}

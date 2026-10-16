using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Gs66Control.Desktop;

public partial class MainWindow
{
    private Process? activeCurveWorker;
    private string? pendingRecoveryId;
    private string? unconfirmedRecoveryId;

    private void RefreshPendingRecovery()
    {
        pendingRecoveryId = null; RecoverCurveButton.Visibility = Visibility.Collapsed;
        RecoverCurveButton.ToolTip = "恢复未完成方案的原曲线与自动模式";
        if (smokeOutput is not null) return;
        if (unconfirmedRecoveryId is string uncheckedId)
        {
            pendingRecoveryId = uncheckedId; RecoverCurveButton.Visibility = Visibility.Visible;
            RecoverCurveButton.ToolTip = "重新核对恢复结果（只读）"; return;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control", "curve-requests");
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(512))
            {
                try
                {
                    var id = Path.GetFileName(directory);
                    if (id == activeCurveId && activeCurveWorker is { HasExited: false }) continue;
                    if (!Guid.TryParseExact(id, "N", out _) || !File.Exists(Path.Combine(directory, "journal.json"))) continue;
                    if (File.Exists(Path.Combine(directory, "result.json")))
                    {
                        using var result = ReadControlJson(Path.Combine(directory, "result.json"));
                        if (result.RootElement.GetProperty("outcome").GetString() != "RecoveryFailed") continue;
                    }
                    using var journal = ReadControlJson(Path.Combine(directory, "journal.json"));
                    var record = journal.RootElement;
                    if (record.GetProperty("requestId").GetString() != id || record.GetProperty("baseline").GetProperty("isSimulated").GetBoolean()) continue;
                    var phase = record.GetProperty("phase").GetString();
                    if (phase is not ("Prepared" or "Writing" or "WritingPerformance" or "Applied" or "Active" or "Restoring" or
                        "ThermalFallbackPrepared" or "ThermalFallbackApplied" or "RestoringPerformance" or "RecoveryPrepared" or "Recovering" or "RecoveryFailed")) continue;
                    pendingRecoveryId = id; RecoverCurveButton.Visibility = Visibility.Visible; return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
                { ActionDiagnosticText.Text = ex.Message; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { ActionDiagnosticText.Text = ex.Message; }
    }

    private async void RecoverCurveClick(object sender, RoutedEventArgs e)
    {
        if (profileActionBusy || fanActionBusy || smokeOutput is not null) return;
        RefreshPendingRecovery();
        if (pendingRecoveryId is not string id) return;
        profileActionBusy = true; RecoverCurveButton.IsEnabled = false;
        try
        {
            if (id == unconfirmedRecoveryId)
            {
                await VerifyCurveRestoredAsync(RequestDirectory("curve", id), id);
                unconfirmedRecoveryId = null; ProfileStatusText.Text = "已恢复自动";
            }
            else await RecoverCurveAsync(id);
        }
        catch (Exception ex) { FanActionText.Text = "恢复未确认"; ActionDiagnosticText.Text = ex.Message; }
        finally { profileActionBusy = false; RecoverCurveButton.IsEnabled = true; RefreshPendingRecovery(); }
    }

    private async Task RecoverCurveAsync(string id)
    {
        FinishTuningRecording();
        var directory = RequestDirectory("curve", id);
        var startedAt = DateTimeOffset.UtcNow;
        using var worker = StartControlWorker("curve-recover", id);
        await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        using var attempt = ReadControlJson(Path.Combine(directory, "recovery-result.json"));
        var root = attempt.RootElement;
        if (worker.ExitCode != 0 || root.GetProperty("requestId").GetString() != id || root.GetProperty("isSimulated").GetBoolean() ||
            root.GetProperty("outcome").GetString() != "Restored" || !root.GetProperty("recoveryVerified").GetBoolean() ||
            root.GetProperty("completedAtUtc").GetDateTimeOffset() < startedAt.AddSeconds(-1))
            throw new IOException(root.GetProperty("reason").GetString() ?? "恢复结果未确认。");
        try { await VerifyCurveRestoredAsync(directory, id); }
        catch { unconfirmedRecoveryId = id; throw; }
        unconfirmedRecoveryId = null;
        activeCurveId = null; activeProfile = null; activeProfileName = null;
        activeCurveWorker?.Dispose(); activeCurveWorker = null;
        curveHeartbeat.Stop(); SetProfileRunning(false); RefreshTrayProfiles();
        ProfileStatusText.Text = ""; FanActionText.Text = "";
        await RefreshPerformanceAsync(); await PreviewFanAsync(); RefreshPendingRecovery();
    }
}

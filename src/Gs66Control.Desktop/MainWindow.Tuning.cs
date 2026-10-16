using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Desktop;

public partial class MainWindow
{
    private ProfileTuningSession? tuningRecording;
    private readonly List<ProfileTuningSummary> tuningSummaries = [];
    private string? tuningStorePath;
    private bool tuningStoreWritable = true;
    private WindowsPowerPlanSnapshot? powerPlanSnapshot;
    private ConsoleProfile? recordedProfile;
    private Guid? recordedPowerPlan;
    private bool tuningStartBusy;

    private sealed record TuningRow(ProfileTuningSummary Summary)
    {
        public string Name => Summary.ProfileName;
        public string Cpu => Temperature(Summary.AverageCpuTemperatureCelsius);
        public string Gpu => Temperature(Summary.AverageGpuTemperatureCelsius);
        public string Fan1 => Number(Summary.AverageCpuFanRpm);
        public string Fan2 => Number(Summary.AverageGpuFanRpm);
        public string Load => Summary.AverageCpuLoadPercent is double value ? $"{value:F0}%" : "—";
        public string Duration => Summary.Duration.TotalHours >= 1 ? Summary.Duration.ToString(@"h\:mm\:ss") : Summary.Duration.ToString(@"mm\:ss");
        private static string Temperature(double? value) => value is double number ? $"{number:F1}°" : "—";
        private static string Number(double? value) => value is double number ? $"{number:N0}" : "—";
    }

    private void InitializeTuningStore(string directory)
    {
        tuningStorePath = Path.Combine(directory, "tuning-records.json");
        if (!File.Exists(tuningStorePath)) return;
        try
        {
            if (new FileInfo(tuningStorePath).Length > 128 * 1024) throw new InvalidDataException("方案记录文件过大。");
            var summaries = JsonSerializer.Deserialize<ProfileTuningSummary[]>(File.ReadAllText(tuningStorePath)) ?? throw new InvalidDataException("方案记录为空。");
            if (summaries.Length > 6 || summaries.Any(s => s is null || s.IsSimulated || string.IsNullOrWhiteSpace(s.ProfileName) ||
                s.ProfileName.Length > 40 || string.IsNullOrWhiteSpace(s.Source) || s.SampleCount is < 1 or > 1200 || s.ComparisonBlockers is null))
                throw new InvalidDataException("方案记录格式不可用。");
            tuningSummaries.AddRange(summaries.Select(s => s.IsAcOnline is null
                ? s with { ComparisonBlockers = s.ComparisonBlockers.Append("旧记录缺少供电状态证据。").Distinct().ToArray() }
                : s)); RefreshTuningTable();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { tuningStoreWritable = false; ActionDiagnosticText.Text = ex.Message; }
    }

    private async void RecordTuningClick(object sender, RoutedEventArgs e)
    {
        if (tuningRecording is not null) { FinishTuningRecording(); return; }
        if (profileActionBusy || fanActionBusy || tuningStartBusy) return;
        if (latest is not { IsSimulated: false } snapshot || DateTimeOffset.UtcNow - snapshot.CapturedAtUtc > TimeSpan.FromSeconds(35)) return;
        var startingProfile = activeProfile;
        tuningStartBusy = true;
        RecordTuningButton.IsEnabled = false;
        try
        {
            var power = await Task.Run(() => new WindowsPowerPlanBackend().Capture(lifetime.Token), lifetime.Token);
            recordedPowerPlan = power.IsSimulated ? null : power.ActiveSchemeGuid;
        }
        catch (Exception ex) { recordedPowerPlan = null; ActionDiagnosticText.Text = ex.Message; }
        finally { RecordTuningButton.IsEnabled = true; tuningStartBusy = false; }
        if (lifetime.IsCancellationRequested || !ReferenceEquals(startingProfile, activeProfile) || profileActionBusy || fanActionBusy) return;
        snapshot = latest ?? snapshot;
        recordedProfile = activeProfile;
        tuningRecording = new(activeProfileName ?? "当前设备", false, snapshot.TemperatureSource + "; " + snapshot.FanSource + "; native Windows system metrics");
        RecordingGlyph.Text = "■"; RecordingGlyph.Foreground = Brushes.Firebrick;
        RecordTuningButton.ToolTip = "停止记录";
        System.Windows.Automation.AutomationProperties.SetName(RecordTuningButton, "停止记录");
        TuningExpander.IsExpanded = true; CollectTuningSnapshot(snapshot);
    }

    private void CollectTuningSnapshot(TelemetrySnapshot snapshot)
    {
        if (tuningRecording is null) return;
        try
        {
            tuningRecording.Add(snapshot);
            var summary = tuningRecording.Summarize();
            var elapsed = summary.Duration.ToString(@"hh\:mm\:ss");
            TuningStatusText.Text = $"{summary.ProfileName} · {elapsed} · {summary.SampleCount}";
            if (summary.SampleCount >= 1200) FinishTuningRecording();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        { FinishTuningRecording(); ActionDiagnosticText.Text = ex.Message; }
    }

    private void FinishTuningRecording()
    {
        if (tuningRecording is null) return;
        var summary = tuningRecording.Summarize() with { AppliedProfile = recordedProfile, WindowsPowerPlanGuid = recordedPowerPlan }; tuningRecording = null;
        RecordingGlyph.Text = "●"; RecordingGlyph.ClearValue(TextBlock.ForegroundProperty);
        RecordTuningButton.ToolTip = "记录当前方案";
        System.Windows.Automation.AutomationProperties.SetName(RecordTuningButton, "记录当前方案");
        if (summary.SampleCount > 0)
        {
            tuningSummaries.Insert(0, summary);
            if (tuningSummaries.Count > 6) tuningSummaries.RemoveAt(tuningSummaries.Count - 1);
            if (tuningStoreWritable && tuningStorePath is not null)
            {
                try { WriteAtomic(tuningStorePath, JsonSerializer.Serialize(tuningSummaries, JsonOptions)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ActionDiagnosticText.Text = ex.Message; }
            }
        }
        RefreshTuningTable(); TuningStatusText.Text = "";
    }

    private void RefreshTuningTable() => TuningTable.ItemsSource = tuningSummaries.Select(s => new TuningRow(s)).ToArray();

    private void TuningSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var rows = TuningTable.SelectedItems.OfType<TuningRow>().Select(r => r.Summary).OrderBy(s => s.FirstSampleAtUtc).ToArray();
        TuningComparisonText.Text = ""; TuningComparisonText.ToolTip = null;
        if (rows.Length != 2) return;
        var comparison = ProfileTuningComparer.Compare(rows[0], rows[1]);
        if (!comparison.IsComparable)
        {
            TuningComparisonText.Text = "样本或负载不适合比较";
            TuningComparisonText.ToolTip = string.Join("\n", comparison.Blockers); return;
        }
        var cpu = rows[1].AverageCpuTemperatureCelsius - rows[0].AverageCpuTemperatureCelsius;
        var rpm = rows[1].AverageCpuFanRpm - rows[0].AverageCpuFanRpm;
        TuningComparisonText.Text = $"{rows[0].ProfileName} → {rows[1].ProfileName} · CPU {cpu:+0.0;-0.0;0.0}° · 风扇 1 {rpm:+0;-0;0} rpm";
    }

    private void ExportTuningClick(object sender, RoutedEventArgs e)
    {
        var summary = (TuningTable.SelectedItem as TuningRow)?.Summary ?? tuningSummaries.FirstOrDefault();
        if (summary is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json|CSV|*.csv", FileName = "gs66-profile-record.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var csv = dialog.FilterIndex == 2;
            File.WriteAllText(dialog.FileName, csv ? ProfileTuningExport.ToCsv(summary) : ProfileTuningExport.ToJson(summary), new UTF8Encoding(csv));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ActionDiagnosticText.Text = ex.Message; }
    }

    private async Task RefreshPowerPlansAsync()
    {
        try
        {
            powerPlanSnapshot = await Task.Run(() => new WindowsPowerPlanBackend().Capture(lifetime.Token), lifetime.Token);
            PowerPlanChoice.ItemsSource = powerPlanSnapshot.Plans; PowerPlanChoice.SelectedValue = powerPlanSnapshot.ActiveSchemeGuid;
            ApplyPowerPlanButton.IsEnabled = true;
            await RefreshCpuPolicyAsync();
        }
        catch (Exception ex)
        { powerPlanSnapshot = null; PowerPlanChoice.ItemsSource = null; ApplyPowerPlanButton.IsEnabled = false; ActionDiagnosticText.Text = ex.Message; }
    }

    private async void ApplyPowerPlanClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || powerPlanSnapshot is null || PowerPlanChoice.SelectedValue is not Guid target || busy || profileActionBusy || fanActionBusy) return;
        var expected = powerPlanSnapshot.ActiveSchemeGuid;
        ApplyPowerPlanButton.IsEnabled = false; FinishTuningRecording();
        try
        {
            var result = await Task.Run(() => new WindowsPowerPlanBackend().Activate(target, expected, lifetime.Token), lifetime.Token);
            if (result.After.IsSimulated || result.After.ActiveSchemeGuid != target) throw new IOException("Windows 电源计划回读失败。");
            await RefreshPowerPlansAsync();
            if (powerPlanSnapshot?.ActiveSchemeGuid != target) throw new IOException("Windows 电源计划已被其他程序更改。");
        }
        catch (Exception ex) { ActionDiagnosticText.Text = ex.Message; }
        finally { ApplyPowerPlanButton.IsEnabled = powerPlanSnapshot is not null; }
    }

    private void RefreshTrayProfiles()
    {
        if (trayProfiles is null) return;
        foreach (var item in trayProfiles.DropDownItems.OfType<System.Windows.Forms.ToolStripItem>().ToArray()) item.Dispose();
        trayProfiles.DropDownItems.Clear();
        foreach (var profile in settings.Profiles)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(profile.Name)
            { Checked = activeProfile is { } current && current.Name == profile.Name && current.PerformanceShift == profile.PerformanceShift && current.Cpu.SequenceEqual(profile.Cpu) && current.Gpu.SequenceEqual(profile.Gpu) };
            item.Click += async (_, _) =>
            {
                try { await ApplyProfileAsync(profile); }
                catch (Exception ex) { ActionDiagnosticText.Text = ex.Message; }
            };
            trayProfiles.DropDownItems.Add(item);
        }
        if (settings.Profiles.Count == 0) trayProfiles.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("未保存方案") { Enabled = false });
        if (activeCurveId is not null)
        {
            trayProfiles.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            var stop = new System.Windows.Forms.ToolStripMenuItem("停止方案");
            stop.Click += (_, _) => StopProfileClick(this, new RoutedEventArgs());
            trayProfiles.DropDownItems.Add(stop);
        }
    }
}

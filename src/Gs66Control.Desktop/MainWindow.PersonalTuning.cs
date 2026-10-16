using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Gs66Control.Core;
using Gs66Control.Windows;
using Forms = System.Windows.Forms;

namespace Gs66Control.Desktop;

public partial class MainWindow
{
    private string CpuUndoPath => Path.Combine(settingsDirectory, "cpu-policy-last-change.json");
    private VendorScenarioSnapshot? savedVendorScenario;
    private DateTimeOffset lastPersonalRefresh;
    private bool personalRefreshBusy;
    private Forms.ToolStripMenuItem? personalTray;

    private PersonalTuningPreset[] PersonalPresets => PersonalTuningPreset.BuiltIns.Concat(settings.CpuPresets).ToArray();
    private static CpuPolicyValues WindowsValues(PersonalCpuPolicy policy) =>
        new(policy.EnergyPreference, policy.BoostMode, policy.MaximumFrequencyMhz, policy.MaximumStatePercent);
    private static PersonalCpuPolicy PersonalValues(CpuPolicyValues policy) =>
        new(policy.EnergyPreference, policy.BoostMode, policy.MaximumFrequencyMhz, policy.MaximumStatePercent);

    private void InitializePersonalTuning(string? selected = null)
    {
        var choices = PersonalPresets;
        PersonalPresetChoice.ItemsSource = choices;
        PersonalPresetChoice.SelectedItem = choices.FirstOrDefault(p => p.Id == (selected ?? settings.LastCpuPresetId)) ?? choices[1];
        PersonalUndoButton.IsEnabled = File.Exists(CpuUndoPath);
    }

    private void OpenPersonalTuningClick(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 4;
    private void PersonalPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PersonalPresetChoice.SelectedItem is not PersonalTuningPreset preset || PersonalPresetDescription is null) return;
        var boostNames = new[] { "关闭", "开启", "积极", "高效", "积极高效", "积极且有保障", "高效且有保障" };
        PersonalPresetDescription.Text = preset.Description + $"\n能效 {preset.Policy.EnergyPreference} · 睿频 {boostNames[preset.Policy.BoostMode]} · 最大状态 {preset.Policy.MaximumStatePercent}%";
        PersonalDeleteButton.IsEnabled = !PersonalTuningPreset.BuiltIns.Any(p => p.Id == preset.Id);
    }

    private async Task RefreshPersonalTuningAsync()
    {
        if (personalRefreshBusy || cpuPolicyBusy) return;
        personalRefreshBusy = true;
        try
        {
            await RefreshCpuPolicyAsync();
            savedVendorScenario = null;
            savedVendorScenario = await Task.Run(WindowsVendorScenarioProbe.Capture, lifetime.Token);
            if (IsFanReaderRunning) await RefreshPerformanceAsync();
            ShowPersonalPolicy();
            RefreshPersonalTray();
        }
        catch (Exception ex) { ShowPersonalPolicy(); PersonalActionStatus.Text = "部分状态未读取：" + ex.Message; }
        finally { lastPersonalRefresh = DateTimeOffset.UtcNow; personalRefreshBusy = false; }
    }

    private void ShowPersonalPolicy()
    {
        if (PersonalFrequencyText is null) return;
        if (cpuPolicySnapshot is not { } state)
        {
            PersonalOverviewText.Text = "参数未读取";
            PersonalFrequencyText.Text = PersonalEppText.Text = "—";
            PersonalEppDetail.Text = "请刷新检查";
            PersonalHealthText.Text = "Windows 参数不可用，请查看设置中的诊断信息。";
            PersonalApplyButton.IsEnabled = false;
            return;
        }
        var actual = PersonalValues(state.Ac);
        var current = PersonalPresets.FirstOrDefault(p => p.Policy == actual && p.Policy.EnergyPreference == state.EffectiveEnergyPreference(true));
        PersonalOverviewText.Text = current?.Name ?? "自定义参数";
        PersonalFrequencyText.Text = actual.MaximumFrequencyMhz == 0 ? "自动" : $"{actual.MaximumFrequencyMhz / 1000d:0.0} GHz";
        PersonalEppText.Text = state.EffectiveEnergyPreference(true).ToString();
        PersonalEppDetail.Text = state.AcOverlay is null ? "基础计划，无滑块覆盖" : $"基础 {state.Ac.EnergyPreference} / 电源滑块 {state.AcOverlay.EnergyPreference}";
        PersonalVendorText.Text = savedVendorScenario?.DisplayName ?? "未读取";
        byte? liveShift = performanceSnapshot is { IsSimulated: false } perf &&
            DateTimeOffset.UtcNow - perf.CapturedAtUtc <= TimeSpan.FromSeconds(15) ? perf.RawShiftValue : null;
        PersonalFirmwareText.Text = liveShift is byte shift ? $"C{shift & 0x0F:X}" : "待启用硬件读数";
        var expected = PersonalPresets.FirstOrDefault(p => p.Id == settings.LastCpuPresetId)?.Policy;
        var warnings = PersonalTuningHealth.Check(actual, state.EffectiveEnergyPreference(true), expected, savedVendorScenario?.Mode, liveShift);
        PersonalHealthText.Text = warnings.Count == 0
            ? "Windows 参数已读取，当前未发现已知的覆盖冲突。"
            : string.Join("\n", warnings);
        var pending = !cpuPolicyBusy && File.Exists(Path.Combine(settingsDirectory, "cpu-policy-pending.json"));
        if (pending)
            PersonalHealthText.Text += "\n上次 CPU 操作留有未完成记录，请核对当前参数后再切换。";
        PersonalHealthText.Foreground = warnings.Count == 0 && !pending ? System.Windows.Media.Brushes.DimGray : System.Windows.Media.Brushes.DarkOrange;
        PersonalApplyButton.IsEnabled = !cpuPolicyBusy;
    }

    private async void RefreshPersonalClick(object sender, RoutedEventArgs e) => await RefreshPersonalTuningAsync();

    private async void ApplyPersonalPresetClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || PersonalPresetChoice.SelectedItem is not PersonalTuningPreset preset) return;
        await ApplyPersonalPresetAsync(preset);
    }

    private async Task ApplyPersonalPresetAsync(PersonalTuningPreset preset)
    {
        if (smokeOutput is not null || cpuPolicyBusy || powerPlanActionBusy || profileActionBusy || fanActionBusy) return;
        try
        {
            PersonalActionStatus.Text = "正在应用并核对…";
            await ApplyCpuTargetAsync(WindowsValues(preset.Policy), true, preset.Id);
            PersonalActionStatus.Text = $"已应用“{preset.Name}”。插电策略已保存到 Windows，风扇与固件档位保持原样。";
            await RefreshPersonalTuningAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { PersonalActionStatus.Text = "已取消，未替你选择其他方案。"; }
        catch (Exception ex) { PersonalActionStatus.Text = "应用未确认：" + ex.Message; ActionDiagnosticText.Text = ex.Message; }
    }

    private async void UndoPersonalClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || cpuPolicyBusy || powerPlanActionBusy || profileActionBusy || fanActionBusy) return;
        try
        {
            if (!File.Exists(CpuUndoPath) || new FileInfo(CpuUndoPath).Length > 64 * 1024) throw new IOException("没有可恢复的参数记录。");
            var change = JsonSerializer.Deserialize<CpuPolicyChange>(File.ReadAllText(CpuUndoPath));
            if (change?.Before is null || change.After is null || change.Before.IsSimulated || change.After.IsSimulated) throw new IOException("恢复记录无效。");
            var values = change.Ac ? change.Before.Ac : change.Before.Dc;
            var id = change.Ac ? PersonalPresets.FirstOrDefault(p => p.Policy == PersonalValues(values) &&
                p.Policy.EnergyPreference == change.Before.EffectiveEnergyPreference(true))?.Id : settings.LastCpuPresetId;
            PersonalActionStatus.Text = "正在核对并恢复上次设置…";
            await ApplyCpuTargetAsync(values, change.Ac, id, change);
            PersonalActionStatus.Text = "已恢复上次参数；如果其他程序已改过设置，本操作会拒绝覆盖。";
            await RefreshPersonalTuningAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { PersonalActionStatus.Text = "已取消恢复。"; }
        catch (Exception ex) { PersonalActionStatus.Text = "未恢复：" + ex.Message; }
    }

    private async void SavePersonalPresetClick(object sender, RoutedEventArgs e)
        => await SavePersonalPresetAsync();

    private async Task SavePersonalPresetAsync()
    {
        if (cpuPolicyBusy || powerPlanActionBusy) return;
        try
        {
            var state = await Task.Run(() => new WindowsCpuPolicyBackend().Capture(), lifetime.Token);
            var name = PersonalPresetName.Text.Trim();
            var existing = settings.CpuPresets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var policy = PersonalValues(state.Ac) with { EnergyPreference = state.EffectiveEnergyPreference(true) };
            var preset = new PersonalTuningPreset(existing?.Id ?? Guid.NewGuid().ToString("N"), name, "从当前实际插电参数保存。", policy);
            var next = settings with { CpuPresets = settings.CpuPresets.Where(p => p.Id != preset.Id).Append(preset).ToArray() };
            store.Save(next); settings = next; InitializePersonalTuning(preset.Id); RefreshPersonalTray();
            PersonalActionStatus.Text = "已保存个人方案；保存方案不会改动当前设备。";
        }
        catch (Exception ex) { PersonalActionStatus.Text = "保存失败：" + ex.Message; }
    }

    private void DeletePersonalPresetClick(object sender, RoutedEventArgs e)
    {
        if (cpuPolicyBusy || PersonalPresetChoice.SelectedItem is not PersonalTuningPreset selected ||
            PersonalTuningPreset.BuiltIns.Any(p => p.Id == selected.Id)) return;
        try
        {
            var next = settings with { CpuPresets = settings.CpuPresets.Where(p => p.Id != selected.Id).ToArray(),
                LastCpuPresetId = settings.LastCpuPresetId == selected.Id ? null : settings.LastCpuPresetId };
            store.Save(next); settings = next; InitializePersonalTuning(); RefreshPersonalTray();
            PersonalActionStatus.Text = "已移除这个方案记录；设备参数保持原样。";
        }
        catch (Exception ex) { PersonalActionStatus.Text = ex.Message; }
    }

    private void AdvancedPersonalClick(object sender, RoutedEventArgs e)
    { Pages.SelectedIndex = 2; CpuPolicyExpander.IsExpanded = true; CpuPowerSourceChoice.SelectedIndex = 0; CpuPolicyExpander.BringIntoView(); }

    private void OpenVendorScenarioClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null) return;
        try { Process.Start(new ProcessStartInfo("msi-dc:") { UseShellExecute = true }); }
        catch (Exception ex) { PersonalActionStatus.Text = "请手动打开 Dragon Center → User Scenario，并保存为平衡模式。"; ActionDiagnosticText.Text = ex.Message; }
    }

    private async void PrepareQuietFanClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || cpuPolicyBusy || powerPlanActionBusy || profileActionBusy || fanActionBusy) return;
        try
        {
            var curves = await CaptureSavedCurvesAsync();
            var draft = ProfileBalance.Create("轻声风扇 " + DateTime.Now.ToString("HH:mm"), curves.Cpu, curves.Gpu, 25);
            deviceCurves = curves; NewProfile(); ProfileName.Text = draft.Name;
            settingBalance = true;
            try { FillCurve(cpuPoints, draft.Cpu); FillCurve(gpuPoints, draft.Gpu); }
            finally { settingBalance = false; }
            SetBalanceIndicator(draft.BalancePosition); SelectShift(ProfilePerformanceChoice, null); Pages.SelectedIndex = 1;
            ProfileStatusText.Text = "已生成较轻声的风扇草稿，尚未应用。温度点与高温保护保持不变。";
        }
        catch (Exception ex) { PersonalActionStatus.Text = "请先启用硬件读数，再准备风扇草稿。"; ActionDiagnosticText.Text = ex.Message; }
    }

    private void RefreshPersonalTray()
    {
        if (personalTray is null) return;
        foreach (var item in personalTray.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
        personalTray.DropDownItems.Clear();
        foreach (var preset in PersonalPresets)
        {
            var choice = new Forms.ToolStripMenuItem(preset.Name) { Checked = cpuPolicySnapshot is { } live &&
                preset.Policy == PersonalValues(live.Ac) && preset.Policy.EnergyPreference == live.EffectiveEnergyPreference(true) };
            choice.Click += (_, _) => Dispatcher.Invoke(() => { _ = ApplyPersonalPresetAsync(preset); });
            personalTray.DropDownItems.Add(choice);
        }
        personalTray.DropDownItems.Add("打开调优页", null, (_, _) => Dispatcher.Invoke(() => { ShowWindow(); Pages.SelectedIndex = 4; }));
    }
}

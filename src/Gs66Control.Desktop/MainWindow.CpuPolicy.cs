using System;
using System.Globalization;
using System.IO;
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
            ShowCpuPolicy(); ApplyCpuPolicyButton.IsEnabled = true;
        }
        catch (Exception ex)
        { cpuPolicySnapshot = null; ApplyCpuPolicyButton.IsEnabled = false; CpuPolicyStatus.Text = "不可用"; ActionDiagnosticText.Text = ex.Message; }
    }

    private void ShowCpuPolicy()
    {
        if (cpuPolicySnapshot is null || CpuPowerSourceChoice is null || CpuEnergyPreference is null) return;
        var values = CpuPowerSourceChoice.SelectedIndex == 1 ? cpuPolicySnapshot.Dc : cpuPolicySnapshot.Ac;
        CpuEnergyPreference.Value = values.EnergyPreference;
        CpuBoostChoice.SelectedIndex = values.BoostMode;
        CpuMaximumState.Text = values.MaximumStatePercent.ToString(CultureInfo.InvariantCulture);
        CpuMaximumFrequency.Text = values.MaximumFrequencyMhz == 0 ? "自动" : values.MaximumFrequencyMhz.ToString(CultureInfo.InvariantCulture);
        CpuPolicyStatus.Text = "";
    }

    private void CpuPowerSourceChanged(object sender, SelectionChangedEventArgs e) => ShowCpuPolicy();
    private async void RefreshCpuPolicyClick(object sender, RoutedEventArgs e)
    { if (!cpuPolicyBusy) await RefreshCpuPolicyAsync(); }

    private async void ApplyCpuPolicyClick(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is not null || cpuPolicySnapshot is null || cpuPolicyBusy || profileActionBusy || fanActionBusy) return;
        cpuPolicyBusy = true; CpuPolicyFields.IsEnabled = false; ApplyCpuPolicyButton.IsEnabled = false; FinishTuningRecording();
        var expected = cpuPolicySnapshot;
        var ac = CpuPowerSourceChoice.SelectedIndex != 1;
        try
        {
            var automaticFrequency = CpuMaximumFrequency.Text.Trim() == "自动";
            var frequency = 0;
            if (!int.TryParse(CpuMaximumState.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) ||
                (!automaticFrequency && !int.TryParse(CpuMaximumFrequency.Text, NumberStyles.None, CultureInfo.InvariantCulture, out frequency)))
                throw new InvalidDataException("请输入整数；频率可填“自动”。");
            var target = new CpuPolicyValues((int)Math.Round(CpuEnergyPreference.Value), CpuBoostChoice.SelectedIndex, frequency, maximum);
            target.ValidateTarget();
            var result = await Task.Run(() =>
            {
                var backend = new WindowsCpuPolicyBackend();
                var fresh = backend.Capture();
                if (!WindowsCpuPolicyBackend.SamePolicy(expected, fresh)) throw new IOException("CPU 策略已改变，请刷新后重试。");
                return backend.Apply(fresh, target, ac);
            }, lifetime.Token);
            var readBack = await Task.Run(() => new WindowsCpuPolicyBackend().Capture(), lifetime.Token);
            if (!WindowsCpuPolicyBackend.SamePolicy(result, readBack)) throw new IOException("CPU 策略回读未确认。");
            cpuPolicySnapshot = readBack; ShowCpuPolicy(); CpuPolicyStatus.Text = "已应用";
        }
        catch (Exception ex) { CpuPolicyStatus.Text = "未确认"; ActionDiagnosticText.Text = ex.Message; }
        finally { cpuPolicyBusy = false; CpuPolicyFields.IsEnabled = true; ApplyCpuPolicyButton.IsEnabled = cpuPolicySnapshot is not null; }
    }
}

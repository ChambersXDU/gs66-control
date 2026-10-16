namespace Gs66Control.Core;

public sealed record PersonalCpuPolicy(int EnergyPreference, int BoostMode, int MaximumFrequencyMhz, int MaximumStatePercent)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (EnergyPreference is < 0 or > 100) errors.Add("能效偏好必须为 0–100。");
        if (BoostMode is < 0 or > 6) errors.Add("睿频模式必须为 0–6。");
        if (MaximumFrequencyMhz != 0 && MaximumFrequencyMhz is < 800 or > 5000) errors.Add("频率上限须为自动或 800–5000 MHz。");
        if (MaximumStatePercent is < 50 or > 100) errors.Add("最大处理器状态必须为 50–100%。");
        return errors;
    }
}

public sealed record PersonalTuningPreset(string Id, string Name, string Description, PersonalCpuPolicy Policy)
{
    public static IReadOnlyList<PersonalTuningPreset> BuiltIns { get; } = Array.AsReadOnly(new[]
    {
        new PersonalTuningPreset("office", "轻声办公", "2.6 GHz · 更偏向能效，待日常体验；可随时恢复上次设置。", new(75, 3, 2600, 100)),
        new PersonalTuningPreset("quiet", "安静均衡", "2.8 GHz · 在响应和发热之间留一些余地。", new(65, 3, 2800, 100)),
        new PersonalTuningPreset("daily", "日常响应", "3.0 GHz · 本机已做短时对照，保留更快的响应。", new(65, 3, 3000, 100))
    });

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80) errors.Add("方案 ID 无效。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40) errors.Add("方案名称须为 1–40 个字符。");
        if (Description is null || Description.Length > 300) errors.Add("方案说明过长。");
        if (Policy is null) errors.Add("CPU 策略缺失。");
        else errors.AddRange(Policy.Validate());
        return errors;
    }
}

public static class PersonalTuningHealth
{
    public static IReadOnlyList<string> Check(PersonalCpuPolicy actual, int effectiveEpp,
        PersonalCpuPolicy? expected, int? savedVendorScenario, byte? currentFirmwareShift)
    {
        var warnings = new List<string>();
        if (actual.EnergyPreference != effectiveEpp)
            warnings.Add($"电源滑块覆盖了能效偏好：基础计划 {actual.EnergyPreference}，当前生效 {effectiveEpp}。");
        if (expected is not null && (actual.BoostMode != expected.BoostMode ||
            actual.MaximumFrequencyMhz != expected.MaximumFrequencyMhz || actual.MaximumStatePercent != expected.MaximumStatePercent ||
            actual.EnergyPreference != expected.EnergyPreference || effectiveEpp != expected.EnergyPreference))
            warnings.Add("Windows 参数与上次应用的个人方案不一致；可以刷新核对后重新应用。");
        if (savedVendorScenario == 4)
            warnings.Add("微星仍保存为节能模式，重启时可能恢复 C2。请在 Dragon Center 中保存为平衡模式。");
        if (currentFirmwareShift == 0xC2)
            warnings.Add("当前硬件处于 C2 省电档，可能明显限制性能。安静方案不会自动选择这个档位。");
        return warnings;
    }
}

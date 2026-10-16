using System.Globalization;
using System.Text.Json;

namespace Gs66Control.Core;

/// <summary>
/// A read-only telemetry recording for one named profile. The caller supplies
/// the source label so an exported result never implies that simulated data is
/// hardware data.
/// </summary>
public sealed class ProfileTuningSession
{
    public static readonly TimeSpan MinimumComparisonDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumAllowedSampleGap = TimeSpan.FromSeconds(30);
    public const int MinimumComparisonValidSamples = 10;
    public const int MaximumSamples = 1200;
    public const double MaximumComparableCpuLoadDifferencePercentagePoints = 10;

    private readonly List<TelemetrySnapshot> samples = [];
    private bool hasTelemetryGap;
    private bool hasPowerSourceChange;

    public ProfileTuningSession(string profileName, bool isSimulated, string source)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("A profile name is required.", nameof(profileName));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("A telemetry source is required.", nameof(source));

        ProfileName = profileName;
        IsSimulated = isSimulated;
        Source = source;
    }

    public string ProfileName { get; }
    public bool IsSimulated { get; }
    public string Source { get; }
    public IReadOnlyList<TelemetrySnapshot> Samples => samples.AsReadOnly();
    public bool HasTelemetryGap => hasTelemetryGap;
    public bool HasPowerSourceChange => hasPowerSourceChange;

    /// <summary>
    /// Adds one truth-stream sample. Missing fields stay missing and do not
    /// become zeroes. Timestamps must be strictly increasing so a session
    /// cannot silently produce a misleading duration or percentile.
    /// </summary>
    public void Add(TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.IsSimulated != IsSimulated)
            throw new InvalidDataException("Telemetry simulation provenance does not match the profile session.");
        if (samples.Count > 0 && snapshot.CapturedAtUtc <= samples[^1].CapturedAtUtc)
            throw new InvalidDataException("Telemetry timestamps must be strictly increasing; duplicate or out-of-order data was rejected.");
        if (samples.Count >= MaximumSamples)
            throw new InvalidDataException("The profile session reached its 1200-sample limit; start a new session instead of dropping old telemetry.");
        if (samples.Count > 0 && snapshot.CapturedAtUtc - samples[^1].CapturedAtUtc > MaximumAllowedSampleGap)
            hasTelemetryGap = true;
        if (samples.Count > 0 &&
            (snapshot.IsAcOnline != samples[^1].IsAcOnline ||
             snapshot.IsBatteryCharging != samples[^1].IsBatteryCharging))
            hasPowerSourceChange = true;

        samples.Add(snapshot);
    }

    public ProfileTuningSummary Summarize()
    {
        var first = samples.Count == 0 ? (DateTimeOffset?)null : samples[0].CapturedAtUtc;
        var last = samples.Count == 0 ? (DateTimeOffset?)null : samples[^1].CapturedAtUtc;
        var duration = first is { } start && last is { } end ? end - start : TimeSpan.Zero;

        var cpuTemperature = Values(snapshot => ValidTemperature(snapshot.CpuTemperatureCelsius));
        var gpuTemperature = Values(snapshot => ValidTemperature(snapshot.GpuTemperatureCelsius));
        var cpuRpm = Values(snapshot => ValidRpm(snapshot.CpuFanRpm));
        var gpuRpm = Values(snapshot => ValidRpm(snapshot.GpuFanRpm));
        var cpuLoad = Values(snapshot => ValidCpuLoad(snapshot.CpuLoadPercent));
        var validSamples = samples.Count(IsCompleteSample);
        var blockers = new List<string>();

        if (duration < MinimumComparisonDuration)
            blockers.Add("至少需要 30 秒连续样本才能比较方案。");
        if (validSamples < MinimumComparisonValidSamples)
            blockers.Add("至少需要 10 个包含 CPU 温度、GPU 温度或经核验 D3 状态、两路转速及 CPU 负载的有效样本。");
        if (hasTelemetryGap)
            blockers.Add("样本之间存在超过 30 秒的 telemetry 断流，不能把记录视为连续工作负载。");
        if (samples.Any(snapshot => snapshot.IsAcOnline is null || snapshot.IsBatteryCharging is null))
            blockers.Add("供电状态（AC 在线/电池充电）存在缺失，不能比较方案。");
        if (hasPowerSourceChange)
            blockers.Add("记录期间 AC 或电池供电状态发生变化，不能比较方案。");

        return new ProfileTuningSummary(
            ProfileName,
            IsSimulated,
            Source,
            first,
            last,
            duration,
            samples.Count,
            validSamples,
            cpuTemperature.Length,
            gpuTemperature.Length,
            cpuRpm.Length,
            gpuRpm.Length,
            cpuLoad.Length,
            hasTelemetryGap,
            samples.Count == 0 ? null : samples[0].IsAcOnline,
            hasPowerSourceChange,
            Average(cpuTemperature),
            Percentile95(cpuTemperature),
            Average(gpuTemperature),
            Percentile95(gpuTemperature),
            Average(cpuRpm),
            Percentile95(cpuRpm),
            Average(gpuRpm),
            Percentile95(gpuRpm),
            Average(cpuLoad),
            blockers) { GpuStandbySampleCount = samples.Count(IsVerifiedStandby) };
    }

    private double[] Values(Func<TelemetrySnapshot, double?> selector) =>
        samples.Select(selector).Where(value => value is not null).Select(value => value!.Value).ToArray();

    private static bool IsCompleteSample(TelemetrySnapshot snapshot) =>
        snapshot.IsAcOnline is not null &&
        snapshot.IsBatteryCharging is not null &&
        ValidTemperature(snapshot.CpuTemperatureCelsius) is not null &&
        (ValidTemperature(snapshot.GpuTemperatureCelsius) is not null || IsVerifiedStandby(snapshot)) &&
        ValidRpm(snapshot.CpuFanRpm) is not null &&
        ValidRpm(snapshot.GpuFanRpm) is not null &&
        ValidCpuLoad(snapshot.CpuLoadPercent) is not null;

    private static bool IsVerifiedStandby(TelemetrySnapshot snapshot)
    {
        if (snapshot.GpuTemperatureCelsius is not null || snapshot.GpuPower is not { } power ||
            power.CapturedAtUtc > snapshot.CapturedAtUtc.AddSeconds(1)) return false;
        var at = power.CapturedAtUtc > snapshot.CapturedAtUtc ? power.CapturedAtUtc : snapshot.CapturedAtUtc;
        return power.IsFreshD3(snapshot.IsSimulated, at);
    }

    private static double? ValidTemperature(int? value) => value is > 0 and <= 125 ? value.Value : null;

    private static double? ValidRpm(int? value) => value is >= 0 and <= 12000 ? value.Value : null;

    private static double? ValidCpuLoad(double? value) =>
        value is double number && double.IsFinite(number) && number is >= 0 and <= 100 ? number : null;

    private static double? Average(IReadOnlyList<double> values) =>
        values.Count == 0 ? null : values.Average();

    /// <summary>
    /// Computes P95 using linear interpolation over the sorted valid values.
    /// One valid value therefore has that value as both its average and P95.
    /// </summary>
    private static double? Percentile95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        if (values.Count == 1) return values[0];

        var sorted = values.OrderBy(value => value).ToArray();
        var position = (sorted.Length - 1) * 0.95;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}

public sealed record ProfileTuningSummary(
    string ProfileName,
    bool IsSimulated,
    string Source,
    DateTimeOffset? FirstSampleAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    TimeSpan Duration,
    int SampleCount,
    int ValidSampleCount,
    int CpuTemperatureSampleCount,
    int GpuTemperatureSampleCount,
    int CpuFanRpmSampleCount,
    int GpuFanRpmSampleCount,
    int CpuLoadSampleCount,
    bool HasTelemetryGap,
    bool? IsAcOnline,
    bool HasPowerSourceChange,
    double? AverageCpuTemperatureCelsius,
    double? P95CpuTemperatureCelsius,
    double? AverageGpuTemperatureCelsius,
    double? P95GpuTemperatureCelsius,
    double? AverageCpuFanRpm,
    double? P95CpuFanRpm,
    double? AverageGpuFanRpm,
    double? P95GpuFanRpm,
    double? AverageCpuLoadPercent,
    IReadOnlyList<string> ComparisonBlockers)
{
    public bool ReadyToCompare => ComparisonBlockers.Count == 0;
    public ConsoleProfile? AppliedProfile { get; init; }
    public Guid? WindowsPowerPlanGuid { get; init; }
    public int GpuStandbySampleCount { get; init; }
}

public sealed record ProfileTuningComparison(
    string BaselineProfileName,
    string CandidateProfileName,
    bool IsSimulated,
    string Source,
    bool IsComparable,
    IReadOnlyList<string> Blockers,
    double? AverageCpuLoadDifferencePercentagePoints);

public static class ProfileTuningComparer
{
    public static ProfileTuningComparison Compare(
        ProfileTuningSummary baseline,
        ProfileTuningSummary candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (baseline.IsSimulated != candidate.IsSimulated)
            throw new InvalidDataException("Simulated and hardware telemetry cannot be compared in one result.");

        var blockers = new List<string>();
        if (!string.Equals(baseline.Source, candidate.Source, StringComparison.Ordinal))
            blockers.Add("方案使用了不同的 telemetry source，无法进行直接比较。");
        AddSummaryBlockers(blockers, baseline, "基准方案");
        AddSummaryBlockers(blockers, candidate, "候选方案");
        if (baseline.HasPowerSourceChange || candidate.HasPowerSourceChange)
            blockers.Add("至少一个方案记录期间供电状态发生变化，无法进行直接比较。");
        if (baseline.IsAcOnline is null || candidate.IsAcOnline is null)
            blockers.Add("方案缺少供电状态证据，无法进行直接比较。");
        if (baseline.IsAcOnline is { } baselineAc && candidate.IsAcOnline is { } candidateAc && baselineAc != candidateAc)
            blockers.Add("基准方案与候选方案使用了不同的 AC 供电状态，无法进行直接比较。");
        if (baseline.SampleCount > 0 && candidate.SampleCount > 0 &&
            Math.Abs((double)baseline.GpuStandbySampleCount / baseline.SampleCount -
                (double)candidate.GpuStandbySampleCount / candidate.SampleCount) > .2)
            blockers.Add("独显休眠占比相差超过 20 个百分点，工作负载不可比。");

        double? loadDifference = null;
        if (baseline.AverageCpuLoadPercent is { } baselineLoad && candidate.AverageCpuLoadPercent is { } candidateLoad)
        {
            loadDifference = Math.Abs(baselineLoad - candidateLoad);
            if (loadDifference > ProfileTuningSession.MaximumComparableCpuLoadDifferencePercentagePoints)
                blockers.Add("CPU 平均负载相差超过 10 个百分点，工作负载不可比。");
        }

        return new(
            baseline.ProfileName,
            candidate.ProfileName,
            baseline.IsSimulated,
            baseline.Source,
            blockers.Count == 0,
            blockers,
            loadDifference);
    }

    private static void AddSummaryBlockers(
        List<string> blockers,
        ProfileTuningSummary summary,
        string label)
    {
        foreach (var blocker in summary.ComparisonBlockers)
            blockers.Add($"{label}：{blocker}");
    }
}

/// <summary>A strongly typed, flat row for one summary CSV export.</summary>
public sealed record ProfileTuningCsvRow(
    string ProfileName,
    bool IsSimulated,
    string Source,
    DateTimeOffset? FirstSampleAtUtc,
    DateTimeOffset? LastSampleAtUtc,
    TimeSpan Duration,
    int SampleCount,
    int ValidSampleCount,
    int CpuTemperatureSampleCount,
    int GpuTemperatureSampleCount,
    int CpuFanRpmSampleCount,
    int GpuFanRpmSampleCount,
    int CpuLoadSampleCount,
    bool HasTelemetryGap,
    bool? IsAcOnline,
    bool HasPowerSourceChange,
    double? AverageCpuTemperatureCelsius,
    double? P95CpuTemperatureCelsius,
    double? AverageGpuTemperatureCelsius,
    double? P95GpuTemperatureCelsius,
    double? AverageCpuFanRpm,
    double? P95CpuFanRpm,
    double? AverageGpuFanRpm,
    double? P95GpuFanRpm,
    double? AverageCpuLoadPercent,
    bool ReadyToCompare,
    string ComparisonBlockers,
    int GpuStandbySampleCount = 0);

public static class ProfileTuningExport
{
    private static readonly string[] CsvHeaders =
    [
        nameof(ProfileTuningCsvRow.ProfileName), nameof(ProfileTuningCsvRow.IsSimulated), nameof(ProfileTuningCsvRow.Source),
        nameof(ProfileTuningCsvRow.FirstSampleAtUtc), nameof(ProfileTuningCsvRow.LastSampleAtUtc), nameof(ProfileTuningCsvRow.Duration),
        nameof(ProfileTuningCsvRow.SampleCount), nameof(ProfileTuningCsvRow.ValidSampleCount),
        nameof(ProfileTuningCsvRow.CpuTemperatureSampleCount), nameof(ProfileTuningCsvRow.GpuTemperatureSampleCount),
        nameof(ProfileTuningCsvRow.CpuFanRpmSampleCount), nameof(ProfileTuningCsvRow.GpuFanRpmSampleCount),
        nameof(ProfileTuningCsvRow.CpuLoadSampleCount), nameof(ProfileTuningCsvRow.HasTelemetryGap),
        nameof(ProfileTuningCsvRow.IsAcOnline), nameof(ProfileTuningCsvRow.HasPowerSourceChange),
        nameof(ProfileTuningCsvRow.AverageCpuTemperatureCelsius),
        nameof(ProfileTuningCsvRow.P95CpuTemperatureCelsius), nameof(ProfileTuningCsvRow.AverageGpuTemperatureCelsius),
        nameof(ProfileTuningCsvRow.P95GpuTemperatureCelsius), nameof(ProfileTuningCsvRow.AverageCpuFanRpm),
        nameof(ProfileTuningCsvRow.P95CpuFanRpm), nameof(ProfileTuningCsvRow.AverageGpuFanRpm),
        nameof(ProfileTuningCsvRow.P95GpuFanRpm), nameof(ProfileTuningCsvRow.AverageCpuLoadPercent),
        nameof(ProfileTuningCsvRow.ReadyToCompare), nameof(ProfileTuningCsvRow.ComparisonBlockers), nameof(ProfileTuningCsvRow.GpuStandbySampleCount)
    ];

    public static string ToJson(ProfileTuningSummary summary, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.Serialize(summary, options ?? new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToJson(ProfileTuningComparison comparison, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return JsonSerializer.Serialize(comparison, options ?? new JsonSerializerOptions { WriteIndented = true });
    }

    public static ProfileTuningCsvRow ToCsvRow(ProfileTuningSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new(
            summary.ProfileName,
            summary.IsSimulated,
            summary.Source,
            summary.FirstSampleAtUtc,
            summary.LastSampleAtUtc,
            summary.Duration,
            summary.SampleCount,
            summary.ValidSampleCount,
            summary.CpuTemperatureSampleCount,
            summary.GpuTemperatureSampleCount,
            summary.CpuFanRpmSampleCount,
            summary.GpuFanRpmSampleCount,
            summary.CpuLoadSampleCount,
            summary.HasTelemetryGap,
            summary.IsAcOnline,
            summary.HasPowerSourceChange,
            summary.AverageCpuTemperatureCelsius,
            summary.P95CpuTemperatureCelsius,
            summary.AverageGpuTemperatureCelsius,
            summary.P95GpuTemperatureCelsius,
            summary.AverageCpuFanRpm,
            summary.P95CpuFanRpm,
            summary.AverageGpuFanRpm,
            summary.P95GpuFanRpm,
            summary.AverageCpuLoadPercent,
            summary.ReadyToCompare,
            string.Join(" | ", summary.ComparisonBlockers), summary.GpuStandbySampleCount);
    }

    public static string ToCsv(ProfileTuningSummary summary)
    {
        var row = ToCsvRow(summary);
        object?[] values =
        [
            row.ProfileName, row.IsSimulated, row.Source, row.FirstSampleAtUtc, row.LastSampleAtUtc,
            row.Duration, row.SampleCount, row.ValidSampleCount, row.CpuTemperatureSampleCount,
            row.GpuTemperatureSampleCount, row.CpuFanRpmSampleCount, row.GpuFanRpmSampleCount,
            row.CpuLoadSampleCount, row.HasTelemetryGap, row.IsAcOnline, row.HasPowerSourceChange,
            row.AverageCpuTemperatureCelsius, row.P95CpuTemperatureCelsius,
            row.AverageGpuTemperatureCelsius, row.P95GpuTemperatureCelsius, row.AverageCpuFanRpm,
            row.P95CpuFanRpm, row.AverageGpuFanRpm, row.P95GpuFanRpm, row.AverageCpuLoadPercent,
            row.ReadyToCompare, row.ComparisonBlockers, row.GpuStandbySampleCount
        ];
        var line = string.Join(",", values.Select(FormatCsvValue));
        return string.Join(Environment.NewLine, [string.Join(",", CsvHeaders), line]) + Environment.NewLine;
    }

    private static string FormatCsvValue(object? value) => value switch
    {
        null => "",
        DateTimeOffset timestamp => Escape(timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        TimeSpan duration => Escape(duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)),
        double number => Escape(number.ToString("G17", CultureInfo.InvariantCulture)),
        IFormattable formattable => Escape(formattable.ToString(null, CultureInfo.InvariantCulture) ?? ""),
        _ => EscapeText(value.ToString() ?? "")
    };

    private static string EscapeText(string value) => Escape(value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value);

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}

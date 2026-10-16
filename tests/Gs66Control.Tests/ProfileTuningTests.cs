using System.Text.Json;
using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class ProfileTuningTests
{
    [Fact]
    public void BrokerPowerEvidenceCanBeSlightlyNewerThanNativeMetricsButNotFarInTheFuture()
    {
        var at = DateTimeOffset.UtcNow;
        var session = new ProfileTuningSession("broker", false, "windows-probe");
        session.Add(Sample(at, gpuTemperature: null) with { GpuPower = new(false, at.AddMilliseconds(20), 4, GpuPowerEvidence.WindowsSource) });
        session.Add(Sample(at.AddSeconds(2), gpuTemperature: null) with { GpuPower = new(false, at.AddSeconds(4), 4, GpuPowerEvidence.WindowsSource) });
        Assert.Equal(1, session.Summarize().ValidSampleCount);
        Assert.Equal(1, session.Summarize().GpuStandbySampleCount);
    }

    [Fact]
    public void VerifiedSleepingGpuSupportsOfficeComparisonWithoutInventedTemperature()
    {
        var started = DateTimeOffset.UtcNow;
        var sleep = new ProfileTuningSession("office", false, "windows-probe");
        var awake = new ProfileTuningSession("awake", false, "windows-probe");
        for (var i = 0; i < 20; i++)
        {
            var at = started.AddSeconds(i * 2);
            sleep.Add(Sample(at, gpuTemperature: null) with { GpuPower = new(false, at, 4, GpuPowerEvidence.WindowsSource) });
            awake.Add(Sample(at));
        }
        var summary = sleep.Summarize();
        Assert.True(summary.ReadyToCompare); Assert.Equal(20, summary.ValidSampleCount);
        Assert.Equal(20, summary.GpuStandbySampleCount); Assert.Null(summary.AverageGpuTemperatureCelsius);
        Assert.True(ProfileTuningComparer.Compare(summary, summary).IsComparable);
        Assert.False(ProfileTuningComparer.Compare(summary, awake.Summarize()).IsComparable);
        Assert.Equal(20, ProfileTuningExport.ToCsvRow(summary).GpuStandbySampleCount);
    }

    [Fact]
    public void RejectsMixedProvenanceAndNonIncreasingTimestamps()
    {
        var started = DateTimeOffset.UtcNow;
        var session = new ProfileTuningSession("工作", false, "windows-probe");
        session.Add(Sample(started, isSimulated: false));

        Assert.Throws<InvalidDataException>(() => session.Add(Sample(started, isSimulated: false)));
        Assert.Throws<InvalidDataException>(() => session.Add(Sample(started.AddSeconds(-1), isSimulated: false)));
        Assert.Throws<InvalidDataException>(() => session.Add(Sample(started.AddSeconds(1), isSimulated: true)));
    }

    [Fact]
    public void MissingReadingsRemainMissingAndDoNotBecomeZero()
    {
        var session = new ProfileTuningSession("缺值", false, "windows-probe");
        session.Add(Sample(
            DateTimeOffset.UtcNow,
            cpuTemperature: 52,
            gpuTemperature: null,
            cpuRpm: null,
            gpuRpm: null,
            cpuLoad: null));

        var summary = session.Summarize();

        Assert.Equal(1, summary.SampleCount);
        Assert.Equal(0, summary.ValidSampleCount);
        Assert.Equal(1, summary.CpuTemperatureSampleCount);
        Assert.Equal(0, summary.GpuTemperatureSampleCount);
        Assert.Equal(0, summary.CpuFanRpmSampleCount);
        Assert.Equal(0, summary.GpuFanRpmSampleCount);
        Assert.Equal(0, summary.CpuLoadSampleCount);
        Assert.Equal(52, summary.AverageCpuTemperatureCelsius);
        Assert.Null(summary.AverageGpuTemperatureCelsius);
        Assert.Null(summary.AverageCpuFanRpm);
        Assert.Null(summary.AverageCpuLoadPercent);
        Assert.False(summary.ReadyToCompare);
    }

    [Fact]
    public void P95UsesLinearInterpolationAndSingleValueBoundary()
    {
        var session = new ProfileTuningSession("P95", false, "windows-probe");
        var started = DateTimeOffset.UtcNow;
        for (var index = 0; index < 20; index++)
        {
            session.Add(Sample(
                started.AddSeconds(index * 2),
                cpuTemperature: 30 + index,
                gpuTemperature: 40 + index,
                cpuRpm: 1000 + index,
                gpuRpm: 1500 + index,
                cpuLoad: 20 + index));
        }

        var summary = session.Summarize();

        Assert.Equal(20, summary.ValidSampleCount);
        Assert.NotNull(summary.AverageCpuTemperatureCelsius);
        Assert.NotNull(summary.P95CpuTemperatureCelsius);
        Assert.Equal(39.5, summary.AverageCpuTemperatureCelsius!.Value);
        Assert.Equal(48.05, summary.P95CpuTemperatureCelsius!.Value, precision: 2);
        Assert.Equal(18.05, summary.P95CpuFanRpm!.Value - 1000, precision: 2);

        var one = new ProfileTuningSession("single", false, "windows-probe");
        one.Add(Sample(started, cpuTemperature: 61, gpuTemperature: 62, cpuRpm: 1200, gpuRpm: 1300, cpuLoad: 40));
        var oneSummary = one.Summarize();
        Assert.NotNull(oneSummary.P95CpuTemperatureCelsius);
        Assert.Equal(61, oneSummary.P95CpuTemperatureCelsius!.Value);
    }

    [Fact]
    public void RequiresThirtySecondsAndTenCompleteSamples()
    {
        var shortSession = new ProfileTuningSession("短记录", false, "windows-probe");
        var shortStarted = DateTimeOffset.UtcNow;
        for (var index = 0; index < 10; index++)
            shortSession.Add(Sample(shortStarted.AddSeconds(index * 2), isSimulated: false));

        var shortSummary = shortSession.Summarize();

        Assert.Equal(10, shortSummary.ValidSampleCount);
        Assert.False(shortSummary.ReadyToCompare);
        Assert.Contains(shortSummary.ComparisonBlockers, blocker => blocker.Contains("30"));

        var longSession = new ProfileTuningSession("完整记录", false, "windows-probe");
        var longStarted = DateTimeOffset.UtcNow;
        for (var index = 0; index < 10; index++)
            longSession.Add(Sample(longStarted.AddSeconds(index * 4), isSimulated: false));

        Assert.True(longSession.Summarize().ReadyToCompare);
    }

    [Fact]
    public void LongSamplingGapBlocksComparisonAndIsMarkedAsDisconnected()
    {
        var session = new ProfileTuningSession("断流", true, "simulation:1s-fixture");
        var started = DateTimeOffset.UtcNow;
        session.Add(Sample(started, isSimulated: true));
        session.Add(Sample(started.AddSeconds(1), isSimulated: true));
        session.Add(Sample(started.AddSeconds(32), isSimulated: true));

        var summary = session.Summarize();

        Assert.True(summary.HasTelemetryGap);
        Assert.False(summary.ReadyToCompare);
        Assert.Contains(summary.ComparisonBlockers, blocker => blocker.Contains("断流"));
    }

    [Fact]
    public void MissingPowerStateBlocksComparisonWithoutInventingItsValue()
    {
        var session = new ProfileTuningSession("缺供电", false, "windows-probe");
        session.Add(Sample(
            DateTimeOffset.UtcNow,
            isAcOnline: true,
            isBatteryCharging: null));

        var summary = session.Summarize();

        Assert.Equal(true, summary.IsAcOnline);
        Assert.False(summary.HasPowerSourceChange);
        Assert.False(summary.ReadyToCompare);
        Assert.Contains(summary.ComparisonBlockers, blocker => blocker.Contains("供电状态"));
        Assert.Equal(0, summary.ValidSampleCount);
    }

    [Fact]
    public void PowerSourceChangeBlocksComparisonAndIsRecorded()
    {
        var session = new ProfileTuningSession("切换供电", false, "windows-probe");
        var started = DateTimeOffset.UtcNow;
        session.Add(Sample(started, isAcOnline: true, isBatteryCharging: false));
        session.Add(Sample(started.AddSeconds(1), isAcOnline: false, isBatteryCharging: false));

        var summary = session.Summarize();

        Assert.Equal(true, summary.IsAcOnline);
        Assert.True(summary.HasPowerSourceChange);
        Assert.False(summary.ReadyToCompare);
        Assert.Contains(summary.ComparisonBlockers, blocker => blocker.Contains("供电状态发生变化"));
    }

    [Fact]
    public void SessionRejectsThe1201stSampleWithoutDroppingTheFirst1200()
    {
        var session = new ProfileTuningSession("长记录", true, "simulation:1s-fixture");
        var started = DateTimeOffset.UtcNow;
        for (var index = 0; index < ProfileTuningSession.MaximumSamples; index++)
            session.Add(Sample(started.AddSeconds(index), isSimulated: true));

        Assert.Equal(ProfileTuningSession.MaximumSamples, session.Samples.Count);
        Assert.Equal(started, session.Samples[0].CapturedAtUtc);
        Assert.Throws<InvalidDataException>(() =>
            session.Add(Sample(started.AddSeconds(ProfileTuningSession.MaximumSamples), isSimulated: true)));
        Assert.Equal(started, session.Samples[0].CapturedAtUtc);
        Assert.Equal(ProfileTuningSession.MaximumSamples, session.Samples.Count);
    }

    [Fact]
    public void ComparisonRejectsMixedSimulationAndLoadDifferenceOverTenPoints()
    {
        var baseline = BuildReadySummary("基准", false, "windows-probe", 50);
        var sameLoad = BuildReadySummary("相同负载", false, "windows-probe", 60);
        var heavyLoad = BuildReadySummary("高负载", false, "windows-probe", 61);
        var simulated = BuildReadySummary("模拟", true, "simulation", 50);

        var comparable = ProfileTuningComparer.Compare(baseline, sameLoad);
        Assert.True(comparable.IsComparable);
        Assert.NotNull(comparable.AverageCpuLoadDifferencePercentagePoints);
        Assert.Equal(10, comparable.AverageCpuLoadDifferencePercentagePoints!.Value);

        var notComparable = ProfileTuningComparer.Compare(baseline, heavyLoad);
        Assert.False(notComparable.IsComparable);
        Assert.Contains(notComparable.Blockers, blocker => blocker.Contains("10"));

        Assert.Throws<InvalidDataException>(() => ProfileTuningComparer.Compare(baseline, simulated));
    }

    [Fact]
    public void ComparisonRejectsDifferentAcStates()
    {
        var ac = BuildReadySummary("交流", false, "windows-probe", 50, isAcOnline: true);
        var battery = BuildReadySummary("电池", false, "windows-probe", 50, isAcOnline: false);

        var comparison = ProfileTuningComparer.Compare(ac, battery);

        Assert.False(comparison.IsComparable);
        Assert.Contains(comparison.Blockers, blocker => blocker.Contains("不同的 AC 供电状态"));
    }

    [Fact]
    public void ComparisonRejectsLegacySummaryWithoutAcStateEvenIfItHasNoStoredBlockers()
    {
        var baseline = BuildReadySummary("基准", false, "windows-probe", 50);
        var legacy = baseline with { ProfileName = "旧记录", IsAcOnline = null, ComparisonBlockers = [] };

        var comparison = ProfileTuningComparer.Compare(baseline, legacy);

        Assert.False(comparison.IsComparable);
        Assert.Contains(comparison.Blockers, blocker => blocker.Contains("供电状态"));
    }

    [Fact]
    public void ExportsExplicitProvenanceAsTypedJsonAndCsv()
    {
        var summary = BuildReadySummary("桌面", false, "windows-probe", 45);

        var json = ProfileTuningExport.ToJson(summary);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("IsSimulated").GetBoolean());
        Assert.Equal("windows-probe", document.RootElement.GetProperty("Source").GetString());
        Assert.True(document.RootElement.GetProperty("IsAcOnline").GetBoolean());

        var csv = ProfileTuningExport.ToCsv(summary);
        Assert.Contains("ProfileName", csv);
        Assert.Contains("AverageCpuLoadPercent", csv);
        Assert.Contains("IsAcOnline", csv);
        Assert.Contains("HasPowerSourceChange", csv);
        Assert.Contains("windows-probe", csv);
        var headers = csv.Split('\n')[0].TrimEnd('\r').Split(',');
        Assert.DoesNotContain(headers, header => header.EndsWith("dB", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("Decibel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("performance score", csv, StringComparison.OrdinalIgnoreCase);
    }

    private static ProfileTuningSummary BuildReadySummary(
        string name,
        bool isSimulated,
        string source,
        double cpuLoad,
        bool? isAcOnline = true,
        bool? isBatteryCharging = false)
    {
        var session = new ProfileTuningSession(name, isSimulated, source);
        var started = DateTimeOffset.UtcNow;
        for (var index = 0; index < 10; index++)
        {
            session.Add(Sample(
                started.AddSeconds(index * 4),
                isSimulated,
                cpuTemperature: 50,
                gpuTemperature: 45,
                cpuRpm: 1800,
                gpuRpm: 1600,
                cpuLoad: cpuLoad,
                isAcOnline: isAcOnline,
                isBatteryCharging: isBatteryCharging));
        }

        return session.Summarize();
    }

    private static TelemetrySnapshot Sample(
        DateTimeOffset capturedAtUtc,
        bool isSimulated = false,
        int? cpuTemperature = 50,
        int? gpuTemperature = 45,
        int? cpuRpm = 1800,
        int? gpuRpm = 1600,
        double? cpuLoad = 50,
        bool? isAcOnline = true,
        bool? isBatteryCharging = false)
    {
        return new(
            capturedAtUtc,
            isSimulated,
            cpuLoad,
            40,
            null,
            cpuRpm,
            gpuRpm,
            [])
        {
            CpuTemperatureCelsius = cpuTemperature,
            GpuTemperatureCelsius = gpuTemperature,
            FanSource = "fixture",
            TemperatureSource = "fixture",
            IsAcOnline = isAcOnline,
            IsBatteryCharging = isBatteryCharging
        };
    }
}

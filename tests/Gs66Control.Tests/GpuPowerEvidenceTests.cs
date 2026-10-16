using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class GpuPowerEvidenceTests
{
    [Fact]
    public void FreshD3SupportsUnavailableTemperatureWithoutInventingOne()
    {
        var now = DateTimeOffset.UtcNow;
        var power = new GpuPowerEvidence(false, now, 4, GpuPowerEvidence.WindowsSource);
        var reading = new ThermalReading(55, null, power);
        Assert.True(reading.GpuWithin(75, false, now)); Assert.Null(reading.GpuTemperature);
        Assert.Empty(AutoFanPolicy.Blockers(State(now) with { GpuTemperature = null, GpuPower = power }, false, now));
    }

    [Theory]
    [InlineData(1, 0, false, true)]
    [InlineData(2, 0, false, true)]
    [InlineData(3, 0, false, true)]
    [InlineData(4, -4, false, true)]
    [InlineData(4, 1, false, true)]
    [InlineData(4, 0, true, true)]
    [InlineData(4, 0, false, false)]
    public void MissingGpuReadingIsRejectedUnlessPowerEvidenceIsCurrentAndVerified(int state, int seconds, bool simulated, bool reviewedSource)
    {
        var now = DateTimeOffset.UtcNow;
        var power = new GpuPowerEvidence(simulated, now.AddSeconds(seconds), state, reviewedSource ? GpuPowerEvidence.WindowsSource : "unknown");
        Assert.False(new ThermalReading(50, null, power).GpuWithin(75, false, now));
        Assert.NotEmpty(AutoFanPolicy.Blockers(State(now) with { GpuTemperature = null, GpuPower = power }, false, now));
    }

    [Fact]
    public void HotOrInvalidTemperatureIsNeverOverriddenByD3()
    {
        var now = DateTimeOffset.UtcNow; var power = new GpuPowerEvidence(false, now, 4, GpuPowerEvidence.WindowsSource);
        foreach (var temperature in new[] { 0, 76, 255 }) Assert.False(new ThermalReading(50, temperature, power).GpuWithin(75, false, now));
        Assert.NotEmpty(AutoFanPolicy.Blockers(State(now) with { CpuTemperature = null, GpuTemperature = null, GpuPower = power }, false, now));
    }

    private static AutoFanState State(DateTimeOffset now) => new(false, now, "Micro-Star International Co., Ltd.",
        "GS66 Stealth 10SE", "E16V1IMS.10C", "16V1EMS1.109", "0311202011:16:49", true, true, 141, 50, 45);
}

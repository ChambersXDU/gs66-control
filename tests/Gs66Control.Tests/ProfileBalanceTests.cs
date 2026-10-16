using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class ProfileBalanceTests
{
    private static readonly CurvePoint[] Cpu = [new(0, 35), new(55, 50), new(60, 60), new(65, 72), new(70, 80), new(90, 85)];
    private static readonly CurvePoint[] Gpu = [new(0, 0), new(50, 50), new(60, 60), new(70, 72), new(82, 80), new(90, 85)];

    [Fact]
    public void KeepsDeviceTemperaturesAndHighTemperatureFloorAtEveryPosition()
    {
        for (var position = 0; position <= 100; position++)
        {
            var profile = ProfileBalance.Create("work", Cpu, Gpu, position);
            Assert.Equal(Cpu.Select(p => p.Temperature), profile.Cpu.Select(p => p.Temperature));
            Assert.Equal(Gpu.Select(p => p.Temperature), profile.Gpu.Select(p => p.Temperature));
            Assert.Empty((ConsoleSettings.Default with { Profiles = [profile] }).Validate());
            Assert.All(profile.Cpu.Concat(profile.Gpu).Where(p => p.Temperature >= 80), p => Assert.InRange(p.Speed, 80, 100));
        }
    }

    [Fact]
    public void MiddlePreservesBaselineAndEndpointsCreateDifferentTargets()
    {
        var middle = ProfileBalance.Create("work", Cpu, Gpu, 50);
        Assert.Equal(Cpu, middle.Cpu); Assert.Equal(Gpu, middle.Gpu);
        var quiet = ProfileBalance.Create("work", Cpu, Gpu, 0);
        var cool = ProfileBalance.Create("work", Cpu, Gpu, 100);
        Assert.True(quiet.Cpu[0].Speed < middle.Cpu[0].Speed);
        Assert.True(cool.Cpu[0].Speed > middle.Cpu[0].Speed);
        Assert.Null(quiet.PerformanceShift);
        Assert.Null(middle.PerformanceShift);
        Assert.Null(cool.PerformanceShift);
        Assert.Equal(35, Cpu[0].Speed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RejectsOutOfRangePosition(int position) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileBalance.Create("work", Cpu, Gpu, position));

    [Fact]
    public void RejectsInvalidBaselineInsteadOfNormalizingIt() =>
        Assert.Throws<InvalidDataException>(() => ProfileBalance.Create("work", Cpu.Skip(1).ToArray(), Gpu, 50));
}

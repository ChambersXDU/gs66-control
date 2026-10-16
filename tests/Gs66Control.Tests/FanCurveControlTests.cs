using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class FanCurveControlTests
{
    private static FanCurveControlBaseline Baseline(byte mode = LegacyFanCurveBinding.ReviewedCustomMode)
    {
        var cpu = new[]
        {
            new CurvePoint(0, 35), new CurvePoint(55, 50), new CurvePoint(60, 60),
            new CurvePoint(65, 72), new CurvePoint(70, 80), new CurvePoint(90, 85)
        };
        var gpu = new[]
        {
            new CurvePoint(0, 0), new CurvePoint(50, 50), new CurvePoint(60, 60),
            new CurvePoint(70, 72), new CurvePoint(82, 80), new CurvePoint(90, 85)
        };
        return new(false, DateTimeOffset.UtcNow, mode, cpu, gpu, "reviewed legacy MSI curve layout");
    }

    [Fact]
    public void BindingMatchesVendorOffsetsAndModeBits()
    {
        Assert.Equal(4, LegacyFanCurveBinding.TemperatureStartIndex);
        Assert.Equal(11, LegacyFanCurveBinding.SpeedStartIndex);
        Assert.Equal(19, LegacyFanCurveBinding.LegacyInstanceCount);
        Assert.Equal(0x8D, LegacyFanCurveBinding.ActivateCustomMode(0x0D));
        Assert.Equal(0x0D, LegacyFanCurveBinding.ActivateAutomaticMode(0x8D));
        Assert.Equal(0x8D, LegacyFanCurveBinding.ActivateCustomMode(0xCD));
    }

    [Fact]
    public void PreviewKeepsFirmwareTemperaturesAndRequiresGuardian()
    {
        var baseline = Baseline(LegacyFanCurveBinding.ReviewedAutomaticMode);
        var cpu = baseline.Cpu.Select(point => point with { Speed = Math.Min(100, point.Speed + 5) }).ToArray();
        var gpu = baseline.Gpu.Select(point => point with { Speed = Math.Min(100, point.Speed + 5) }).ToArray();

        var plan = FanCurveControlPolicy.Preview(baseline, cpu, gpu, false, DateTimeOffset.UtcNow);

        Assert.True(plan.Eligible);
        Assert.Equal(LegacyFanCurveBinding.ReviewedCustomMode, plan.TargetMode);
        Assert.True(plan.RequiresRollbackJournal);
        Assert.True(plan.RequiresIndependentRecovery);
        Assert.Equal(baseline.Cpu.Select(point => point.Temperature), plan.TargetCpu.Select(point => point.Temperature));
        Assert.Equal(baseline.Gpu.Select(point => point.Temperature), plan.TargetGpu.Select(point => point.Temperature));
    }

    [Fact]
    public void RejectsTemperatureChangesInsteadOfTreatingThemAsWritable()
    {
        var baseline = Baseline();
        var target = baseline.Cpu.ToArray();
        target[3] = target[3] with { Temperature = 75 };

        var blockers = FanCurveControlPolicy.Blockers(baseline, target, baseline.Gpu, false, DateTimeOffset.UtcNow);

        Assert.Contains(blockers, blocker => blocker.Contains("temperature breakpoints", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnsafeHighTemperatureSpeedFloor()
    {
        var baseline = Baseline();
        var target = baseline.Gpu.ToArray();
        target[4] = target[4] with { Speed = 75 };
        target[5] = target[5] with { Speed = 75 };

        var blockers = FanCurveControlPolicy.Blockers(baseline, baseline.Cpu, target, false, DateTimeOffset.UtcNow);

        Assert.Contains(blockers, blocker => blocker.Contains("at least 80%", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnreviewedModeStaleStateAndProvenanceMismatch()
    {
        var baseline = Baseline(0x4D) with
        {
            IsSimulated = true,
            CapturedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-11)
        };

        var blockers = FanCurveControlPolicy.Blockers(baseline, baseline.Cpu, baseline.Gpu, false, DateTimeOffset.UtcNow);

        Assert.Contains(blockers, blocker => blocker.Contains("provenance", StringComparison.Ordinal));
        Assert.Contains(blockers, blocker => blocker.Contains("stale", StringComparison.Ordinal));
        Assert.Contains(blockers, blocker => blocker.Contains("reviewed", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsNonMonotonicOrOutOfRangeSpeedValues()
    {
        var baseline = Baseline();
        var target = baseline.Cpu.ToArray();
        target[2] = target[2] with { Speed = 40 };

        var blockers = FanCurveControlPolicy.Blockers(baseline, target, baseline.Gpu, false, DateTimeOffset.UtcNow);

        Assert.Contains(blockers, blocker => blocker.Contains("non-decreasing", StringComparison.Ordinal));
    }
}

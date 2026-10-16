using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class PerformanceModeTests
{
    [Theory]
    [InlineData(Gs66PerformanceMode.ExtremePerformance, Gs66ShiftType.SportMode)]
    [InlineData(Gs66PerformanceMode.Balanced, Gs66ShiftType.GreenMode)]
    [InlineData(Gs66PerformanceMode.Silent, Gs66ShiftType.GreenMode)]
    [InlineData(Gs66PerformanceMode.SuperBattery, Gs66ShiftType.Eco)]
    public void BuiltInScenariosUseReviewedShiftMapping(Gs66PerformanceMode mode, Gs66ShiftType shift)
    {
        var spec = Gs66PerformancePolicy.Resolve(mode);
        Assert.Equal(shift, spec.ShiftType);
        Assert.False(spec.IsCustomizable);
        Assert.False(spec.ChangesWindowsPowerPlan);
    }

    [Fact]
    public void CustomizeKeepsCallerSelectedShift()
    {
        var spec = Gs66PerformancePolicy.Resolve(Gs66PerformanceMode.Customize, Gs66ShiftType.User);
        Assert.Equal(Gs66ShiftType.User, spec.ShiftType);
        Assert.True(spec.IsCustomizable);
    }

    [Theory]
    [InlineData(Gs66ShiftType.SportMode, 0xC4)]
    [InlineData(Gs66ShiftType.ComfortMode, 0xC0)]
    [InlineData(Gs66ShiftType.GreenMode, 0xC1)]
    [InlineData(Gs66ShiftType.Eco, 0xC2)]
    [InlineData(Gs66ShiftType.User, 0xC3)]
    public void ShiftEncodingPreservesActivationBits(Gs66ShiftType shift, byte expected)
    {
        var encoded = Gs66ShiftState.Encode(shift, 0x3F);
        Assert.Equal(expected, encoded);
        var state = new Gs66ShiftState(encoded);
        Assert.True(state.HasShiftSupport);
        Assert.True(state.IsActive);
        Assert.Equal(shift, state.ShiftType);
    }

    [Fact]
    public void PerformancePolicyRequiresIndependentReadOnlyBindingBeforeApply()
    {
        var spec = Gs66PerformancePolicy.Resolve(Gs66PerformanceMode.ExtremePerformance);
        var blockers = Gs66PerformancePolicy.Blockers(
            spec,
            new Gs66ShiftState(0xC1),
            deviceBindingVerified: false,
            legacyShiftPathAvailable: true,
            isSimulated: false);
        Assert.Contains("Device, BIOS and EC binding is not verified.", blockers);
    }

    [Fact]
    public void SimulatedEvidenceCannotAuthorizeApply()
    {
        var spec = Gs66PerformancePolicy.Resolve(Gs66PerformanceMode.Balanced);
        var blockers = Gs66PerformancePolicy.Blockers(
            spec,
            new Gs66ShiftState(0xC1),
            deviceBindingVerified: true,
            legacyShiftPathAvailable: true,
            isSimulated: true);
        Assert.Contains("Simulated evidence cannot authorize a hardware mode change.", blockers);
    }

    [Fact]
    public void HighPerformancePlanRequiresPresenceAndRetainsPreviousPlan()
    {
        var previous = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var plan = WindowsPowerPlanPlan.Create(
            previous,
            [previous, WindowsPowerPlanPlan.DragonCenterHighPerformancePlan],
            requestHighPerformance: true);
        Assert.True(plan.CanApply);
        Assert.Equal(previous, plan.PreviousPlan);
        Assert.Equal(WindowsPowerPlanPlan.DragonCenterHighPerformancePlan, plan.RequestedPlan);
    }

    [Fact]
    public void MissingHighPerformancePlanIsRejectedWithoutInventingOne()
    {
        var previous = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var plan = WindowsPowerPlanPlan.Create(previous, [previous], requestHighPerformance: true);
        Assert.False(plan.CanApply);
        Assert.False(plan.TargetIsAvailable);
        Assert.Contains("The reviewed high-performance plan is not present.", plan.Blockers);
    }
}

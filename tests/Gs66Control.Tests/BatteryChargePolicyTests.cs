using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class BatteryChargePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static BatteryChargeGetDataObservation Valid(byte value = 0xD0) =>
        new(
            IsHardware: true,
            IsD7GetDataProof: true,
            InvocationSucceeded: true,
            Flag: 1,
            Data: [value],
            CapturedAtUtc: Now,
            Source: "MSI_ACPI.Get_Data(0xD7)");

    [Theory]
    [InlineData(0xE4, BatteryChargeLimit.Balanced, 0xD0)]
    [InlineData(0x64, BatteryChargeLimit.BestForBattery, 0x3C)]
    [InlineData(0xE4, BatteryChargeLimit.BestForMobility, 0xE4)]
    public void TargetPreservesBit7AndReplacesOnlyTheReviewedLimit(byte current, BatteryChargeLimit target, byte expected)
    {
        Assert.True(BatteryChargePolicy.TryEncodeTarget(current, target, out var encoded));
        Assert.Equal(expected, encoded);
        Assert.Equal(current & BatteryChargePolicy.Bit7Mask, encoded & BatteryChargePolicy.Bit7Mask);
    }

    [Fact]
    public void AFreshHardwareD7ObservationMakesTheFixedPolicyEligible()
    {
        var plan = BatteryChargePolicy.Preview(Valid(), BatteryChargeLimit.BestForBattery, Now);

        Assert.True(plan.Eligible);
        Assert.Equal(BatteryChargeLimit.Balanced, plan.CurrentLimit);
        Assert.Equal((byte)0xD0, plan.CurrentRawValue);
        Assert.Equal((byte)0xBC, plan.ProposedRawValue);
        Assert.True(plan.RequiresImmediateReadback);
        Assert.True(plan.RequiresIndependentRecovery);
    }

    [Fact]
    public void MissingObservationNeverBecomesEligible()
    {
        var plan = BatteryChargePolicy.Preview(null, BatteryChargeLimit.Balanced, Now);

        Assert.False(plan.Eligible);
        Assert.Null(plan.CurrentRawValue);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("fresh hardware", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void NonSuccessFlagsAreRejected(byte flag)
    {
        var plan = BatteryChargePolicy.Preview(Valid() with { Flag = flag }, BatteryChargeLimit.Balanced, Now);

        Assert.False(plan.Eligible);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("successful flag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonHardwareOrUnprovenObservationsAreRejected()
    {
        Assert.False(BatteryChargePolicy.Preview(Valid() with { IsHardware = false }, BatteryChargeLimit.Balanced, Now).Eligible);
        Assert.False(BatteryChargePolicy.Preview(Valid() with { IsD7GetDataProof = false }, BatteryChargeLimit.Balanced, Now).Eligible);
        Assert.False(BatteryChargePolicy.Preview(Valid() with { InvocationSucceeded = false }, BatteryChargeLimit.Balanced, Now).Eligible);
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void InvalidPayloadShapesAreRejected(IReadOnlyList<byte>? payload)
    {
        var plan = BatteryChargePolicy.Preview(Valid() with { Data = payload }, BatteryChargeLimit.Balanced, Now);

        Assert.False(plan.Eligible);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("exactly one", StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<object?[]> InvalidPayloads() =>
    [
        new object?[] { null },
        new object?[] { Array.Empty<byte>() },
        new object?[] { new byte[] { 0xE4, 0x00 } }
    ];

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x46)]
    [InlineData(0x7F)]
    [InlineData(0xC6)]
    public void UnknownCurrentLimitsAreRejected(byte current)
    {
        var plan = BatteryChargePolicy.Preview(Valid(current), BatteryChargeLimit.Balanced, Now);

        Assert.False(plan.Eligible);
        Assert.Null(plan.CurrentLimit);
        Assert.Null(plan.ProposedRawValue);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        Assert.False(BatteryChargePolicy.TryEncodeTarget(current, BatteryChargeLimit.Balanced, out _));
    }

    [Fact]
    public void InvalidRequestedLimitIsRejected()
    {
        var plan = BatteryChargePolicy.Preview(Valid(), (BatteryChargeLimit)70, Now);

        Assert.False(plan.Eligible);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("60/80/100", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StaleFutureAndUnlabelledEvidenceAreRejected()
    {
        Assert.False(BatteryChargePolicy.Preview(Valid() with { CapturedAtUtc = Now.AddSeconds(-11) }, BatteryChargeLimit.Balanced, Now).Eligible);
        Assert.False(BatteryChargePolicy.Preview(Valid() with { CapturedAtUtc = Now.AddSeconds(1) }, BatteryChargeLimit.Balanced, Now).Eligible);
        Assert.False(BatteryChargePolicy.Preview(Valid() with { Source = "" }, BatteryChargeLimit.Balanced, Now).Eligible);
    }
}

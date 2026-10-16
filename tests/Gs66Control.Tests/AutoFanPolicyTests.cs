using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class AutoFanPolicyTests
{
    private static AutoFanState Valid => new(true, DateTimeOffset.UtcNow,
        "Micro-Star International Co., Ltd.", "GS66 Stealth 10SE", "E16V1IMS.10C", "16V1EMS1.109", "0311202011:16:49",
        true, true, 141, 50, 42);

    [Fact]
    public void PreviewIsExplicitlySimulatedAndHasNoWriteAuthorization()
    {
        var preview = AutoFanPolicy.Preview(Valid, true, DateTimeOffset.UtcNow);
        Assert.True(preview.Eligible);
        Assert.True(preview.State.IsSimulated);
        Assert.Equal(13, preview.ProposedMode);
        Assert.False(preview.FirstHardwareWriteApproved);
        Assert.Equal(0, preview.HardwareWrites);
    }

    [Fact]
    public void ChangedIdentityFirmwareSchemaAndArtifactAreAllRejected()
    {
        var invalid = new[] { Valid with { Model = "GS66 Stealth 11UE" }, Valid with { BiosVersion = "E16V1IMS.10D" },
            Valid with { EcVersion = "16V1EMS1.110" }, Valid with { EcBuild = "0000000000:00:00" },
            Valid with { SchemaMatches = false }, Valid with { VendorArtifactMatches = false },
            Valid with { Manufacturer = "Unknown" } };
        foreach (var state in invalid) Assert.False(AutoFanPolicy.Preview(state, true, DateTimeOffset.UtcNow).Eligible);
    }

    [Theory]
    [InlineData(0)] [InlineData(77)] [InlineData(205)] [InlineData(255)]
    public void OtherFanStatesAreRejected(byte mode) =>
        Assert.False(AutoFanPolicy.Preview(Valid with { Mode = mode }, true, DateTimeOffset.UtcNow).Eligible);

    [Theory]
    [InlineData(null, 42)] [InlineData(50, null)] [InlineData(81, 42)] [InlineData(50, 76)] [InlineData(0, 42)]
    public void MissingOrHotTelemetryPreventsTrial(int? cpu, int? gpu) =>
        Assert.False(AutoFanPolicy.Preview(Valid with { CpuTemperature = cpu, GpuTemperature = gpu }, true, DateTimeOffset.UtcNow).Eligible);

    [Fact]
    public void RecoveryToAutomaticCanProceedWithoutTemperatureButStillRequiresIdentityAndMode()
    {
        Assert.Empty(AutoFanPolicy.Blockers(Valid with { Mode = 13, CpuTemperature = null, GpuTemperature = null }, true, DateTimeOffset.UtcNow, false));
        Assert.NotEmpty(AutoFanPolicy.Blockers(Valid with { BiosVersion = "unknown", CpuTemperature = null }, true, DateTimeOffset.UtcNow, false));
        Assert.NotEmpty(AutoFanPolicy.Blockers(Valid with { Mode = 77 }, true, DateTimeOffset.UtcNow, false));
    }

    [Fact]
    public void StaleFutureAndSimulatedHardwareEvidenceAreRejected()
    {
        Assert.False(AutoFanPolicy.Preview(Valid with { CapturedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-11) }, true, DateTimeOffset.UtcNow).Eligible);
        Assert.False(AutoFanPolicy.Preview(Valid with { CapturedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) }, true, DateTimeOffset.UtcNow).Eligible);
        Assert.False(AutoFanPolicy.Preview(Valid, false, DateTimeOffset.UtcNow).Eligible);
        Assert.False(AutoFanPolicy.Preview(Valid with { IsSimulated = false }, true, DateTimeOffset.UtcNow).Eligible);
    }
}

using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class CpuPolicyActionContractTests
{
    private static CpuPolicyActionRequest Request()
    {
        var now = DateTimeOffset.UtcNow;
        var values = new CpuPolicyValues(65, 3, 3000, 100);
        var snapshot = new CpuPolicySnapshot(Guid.NewGuid(), values, values, 5, 5, 1, 1, now, false);
        return new(Guid.NewGuid(), snapshot, values with { MaximumFrequencyMhz = 2800 }, true, null, now, 123, now.AddMinutes(-1));
    }

    [Fact]
    public void AcceptsAFreshBoundedRealRequest() => Request().Validate(DateTimeOffset.UtcNow);

    [Fact]
    public void RejectsExpiredAndFutureRequests()
    {
        var request = Request();
        Assert.Throws<InvalidDataException>(() => (request with { RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3), ParentStartUtc = DateTimeOffset.UtcNow.AddMinutes(-5) }).Validate(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) }).Validate(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsSimulationAsEitherCurrentStateOrRecoveryState()
    {
        var request = Request(); var simulated = request.Expected with { IsSimulated = true };
        Assert.Throws<InvalidDataException>(() => (request with { Expected = simulated }).Validate(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { RestorePrevious = simulated }).Validate(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsMissingIdentityAndOutOfBoundsPolicy()
    {
        var request = Request();
        Assert.Throws<InvalidDataException>(() => (request with { RequestId = Guid.Empty }).Validate(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { ParentPid = 0 }).Validate(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { ParentStartUtc = request.RequestedAtUtc.AddSeconds(2) }).Validate(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidDataException>(() => (request with { Target = request.Target with { EnergyPreference = 101 } }).Validate(DateTimeOffset.UtcNow));
    }
}

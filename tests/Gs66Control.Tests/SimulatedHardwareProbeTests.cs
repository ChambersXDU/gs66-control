using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class SimulatedHardwareProbeTests
{
    [Fact]
    public async Task ReportsClearlyMarkedSimulation()
    {
        var snapshot = await new SimulatedHardwareProbe().CaptureAsync();

        Assert.True(snapshot.IsSimulated);
        Assert.Equal("simulation", snapshot.Source);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("Simulated", StringComparison.OrdinalIgnoreCase));
        Assert.Null(snapshot.BiosVersion);
        Assert.Empty(snapshot.MsiWmiClasses);
    }

    [Fact]
    public async Task HonorsCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SimulatedHardwareProbe().CaptureAsync(cts.Token));
    }
}

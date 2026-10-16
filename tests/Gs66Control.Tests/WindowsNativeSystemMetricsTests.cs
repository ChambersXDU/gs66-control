using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class WindowsNativeSystemMetricsTests
{
    [Fact]
    public void FirstCpuSampleIsHonestNull()
    {
        var current = new NativeCpuTimes(Idle: 100, Kernel: 300, User: 200);

        Assert.Null(WindowsNativeSystemMetricsMath.ComputeCpuUsage(null, current));
    }

    [Fact]
    public void CpuUsageUsesCounterDeltas()
    {
        var previous = new NativeCpuTimes(Idle: 100, Kernel: 300, User: 200);
        var current = new NativeCpuTimes(Idle: 200, Kernel: 500, User: 400);

        Assert.Equal(75d, WindowsNativeSystemMetricsMath.ComputeCpuUsage(previous, current));
    }

    [Fact]
    public void RegressedCountersAreUnavailable()
    {
        var previous = new NativeCpuTimes(Idle: 200, Kernel: 500, User: 400);
        var current = new NativeCpuTimes(Idle: 100, Kernel: 450, User: 350);

        Assert.Null(WindowsNativeSystemMetricsMath.ComputeCpuUsage(previous, current));
    }
}

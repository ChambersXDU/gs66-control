using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsFanTelemetryProbe
{
    public static FanTelemetryFrame Capture(string requestId)
    {
        var values = MsiWmiProperties.ReadFanCounters(CancellationToken.None);
        var cpu = FanCounter.ToRpm(values[2], values[3]);
        var gpu = FanCounter.ToRpm(values[4], values[5]);
        var cpuTemperature = MsiTemperature.ToCelsius(MsiWmiProperties.ReadIndexedBytes("MSI_CPU", "CPU", 19, CancellationToken.None)[1]);
        var gpuTemperature = MsiTemperature.ToCelsius(MsiWmiProperties.ReadIndexedBytes("MSI_VGA", "VGA", 18, CancellationToken.None)[1]);
        var warnings = new List<string>();
        GpuPowerEvidence? power = null;
        try { power = new WindowsGpuPowerProbe().Capture(); }
        catch (Exception ex) { if (gpuTemperature is null) warnings.Add("GPU device power state unavailable: " + ex.Message); }
        if (cpu is null || gpu is null) warnings.Add("MSI fan counter conversion is outside the plausible range.");
        if (cpuTemperature is null) warnings.Add("CPU 温度当前不可用。");
        if (gpuTemperature is null && power?.IsFreshD3(false, DateTimeOffset.UtcNow) != true) warnings.Add("GPU 温度当前不可用。");
        return new(requestId, false, DateTimeOffset.UtcNow, cpu, gpu, FanTelemetryFrame.ReviewedSource,
            warnings) { CpuTemperatureCelsius = cpuTemperature, GpuTemperatureCelsius = gpuTemperature, GpuPower = power };
    }
}

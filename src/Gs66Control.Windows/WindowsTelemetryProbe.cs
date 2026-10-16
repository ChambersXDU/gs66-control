using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsTelemetryProbe(HardwareSnapshot identity) : ITelemetryProbe
{
    private readonly WindowsNativeSystemMetrics systemMetrics = new();
    private bool fanReadDenied;
    private DateTimeOffset nextFanReadAttempt;
    public Task<TelemetrySnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private TelemetrySnapshot Capture(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var native = systemMetrics.Capture();
        var warnings = new List<string>(native.Warnings);
        var cpu = native.CpuUsagePercent;
        var memory = native.MemoryUsagePercent;
        var battery = native.BatteryPercent;
        int? fan1 = null, fan2 = null;
        var fanReadRequiresElevation = false;
        var source = "Unavailable";
        int? cpuTemperature = null, gpuTemperature = null;
        var temperatureSource = "Unavailable";
        GpuPowerEvidence? gpuPower = null;
        // This binding is read-only and verified on this model/BIOS. It is not a write allowlist.
        if (!identity.IsSimulated && identity.Model == "GS66 Stealth 10SE" && identity.BiosVersion == "E16V1IMS.10C")
        {
            if (fanReadDenied && DateTimeOffset.UtcNow < nextFanReadAttempt)
            {
                fanReadRequiresElevation = true;
                warnings.Add("MSI fan telemetry unavailable: Windows permission is required for direct counter reads.");
            }
            else try
            {
                var bytes = MsiWmiProperties.ReadFanCounters(token);
                fanReadDenied = false;
                fan1 = FanCounter.ToRpm(bytes[2], bytes[3]);
                fan2 = FanCounter.ToRpm(bytes[4], bytes[5]);
                source = "MSI_AP counters; vendor-derived RPM (two logical channels)";
                if (fan1 is null || fan2 is null) warnings.Add("MSI fan counter conversion is outside the plausible range.");
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidDataException)
            {
                fanReadRequiresElevation = ex is UnauthorizedAccessException ||
                    ex is ManagementException { ErrorCode: ManagementStatus.AccessDenied } ||
                    ex is COMException { HResult: unchecked((int)0x80070005) };
                fanReadDenied = fanReadRequiresElevation;
                nextFanReadAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
                warnings.Add($"MSI fan telemetry unavailable: {ex.Message}");
            }
            cpuTemperature = Temperature("MSI_CPU", "CPU", 19, "CPU", warnings, token);
            gpuTemperature = Temperature("MSI_VGA", "VGA", 18, "GPU", warnings, token);
            try { gpuPower = new WindowsGpuPowerProbe().Capture(); }
            catch (Exception ex) when (ex is ManagementException or IOException or InvalidDataException or UnauthorizedAccessException or COMException)
            { if (gpuTemperature is null) warnings.Add("GPU device power state unavailable: " + ex.Message); }
            if (gpuTemperature is null && gpuPower?.IsFreshD3(false, DateTimeOffset.UtcNow) == true)
                warnings.RemoveAll(w => w.StartsWith("GPU 温度当前不可用", StringComparison.Ordinal));
            temperatureSource = "MSI_CPU/MSI_VGA index 1; installed vendor System Monitor binding";
        }
        else warnings.Add("MSI fan/temperature bindings are not verified for this model/BIOS.");
        return new(DateTimeOffset.UtcNow, false, InRange(cpu, 0, 100), memory,
            battery is >= 0 and <= 100 ? battery.Value : null, fan1, fan2, warnings)
        {
            IsAcOnline = native.IsAcOnline,
            IsBatteryCharging = native.IsBatteryCharging,
            FanSource = source,
            FanReadRequiresElevation = fanReadRequiresElevation,
            CpuTemperatureCelsius = cpuTemperature,
            GpuTemperatureCelsius = gpuTemperature,
            GpuPower = gpuPower,
            TemperatureSource = temperatureSource
        };
    }

    private static int? Temperature(string className, string property, int expectedCount, string label, List<string> warnings, CancellationToken token)
    {
        try
        {
            var value = MsiTemperature.ToCelsius(MsiWmiProperties.ReadIndexedBytes(className, property, expectedCount, token)[1]);
            if (value is null) warnings.Add($"{label} 温度当前不可用（原厂返回未知值或超出可用读数范围）。");
            return value;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidDataException)
        { warnings.Add($"{label} temperature unavailable: {ex.Message}"); return null; }
    }

    private static double? InRange(double? value, double min, double max) => value >= min && value <= max ? value : null;

}

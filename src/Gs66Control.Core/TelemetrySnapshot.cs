namespace Gs66Control.Core;

public sealed record TelemetrySnapshot(
    DateTimeOffset CapturedAtUtc,
    bool IsSimulated,
    double? CpuLoadPercent,
    double? MemoryUsedPercent,
    int? BatteryPercent,
    int? CpuFanRpm,
    int? GpuFanRpm,
    IReadOnlyList<string> Warnings)
{
    public string FanSource { get; init; } = "Unavailable";
    public bool FanReadRequiresElevation { get; init; }
    public int? CpuTemperatureCelsius { get; init; }
    public int? GpuTemperatureCelsius { get; init; }
    public GpuPowerEvidence? GpuPower { get; init; }
    public string TemperatureSource { get; init; } = "Unavailable";
    public bool? IsAcOnline { get; init; }
    public bool? IsBatteryCharging { get; init; }
}

public interface ITelemetryProbe
{
    Task<TelemetrySnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public static class FanCounter
{
    public static int? ToRpm(byte low, byte high)
    {
        var counter = (high << 8) | low;
        if (counter == 0) return 0;
        var rpm = 480000 / counter;
        return rpm <= 12000 ? rpm : null;
    }
}

public static class MsiTemperature
{
    public static int? ToCelsius(byte value) => value is > 0 and <= 125 ? value : null;
}

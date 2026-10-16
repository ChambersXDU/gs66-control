namespace Gs66Control.Core;

public sealed record GpuPowerEvidence(bool IsSimulated, DateTimeOffset CapturedAtUtc, int DevicePowerState, string Source)
{
    public const string WindowsSource = "Windows CM_POWER_DATA; NVIDIA GeForce RTX 2060";
    public bool IsFreshD3(bool simulation, DateTimeOffset now) => IsSimulated == simulation && Source == WindowsSource &&
        DevicePowerState == 4 && now - CapturedAtUtc >= TimeSpan.Zero && now - CapturedAtUtc <= TimeSpan.FromSeconds(3);
}

public sealed record ThermalReading(int? CpuTemperature, int? GpuTemperature, GpuPowerEvidence? GpuPower = null)
{
    public bool GpuWithin(int maximum, bool simulation, DateTimeOffset now) => GpuTemperature is int value
        ? value > 0 && value <= maximum : GpuPower?.IsFreshD3(simulation, now) == true;
}

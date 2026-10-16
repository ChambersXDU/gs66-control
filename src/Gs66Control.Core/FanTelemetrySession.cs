namespace Gs66Control.Core;

public sealed record FanTelemetryRequest(string RequestId, int ParentPid, DateTimeOffset ParentStartUtc)
{
    public void Validate()
    {
        if (!Guid.TryParseExact(RequestId, "N", out var id) || id == Guid.Empty || ParentPid <= 0 || ParentStartUtc == default)
            throw new InvalidDataException("Invalid read-only fan session identity.");
    }
}

public sealed record FanTelemetryFrame(string RequestId, bool IsSimulated, DateTimeOffset CapturedAtUtc,
    int? CpuFanRpm, int? GpuFanRpm, string Source, IReadOnlyList<string> Warnings)
{
    public const string ReviewedSource = "MSI_AP[2..5]; direct read; vendor RPM conversion";
    public int? CpuTemperatureCelsius { get; init; }
    public int? GpuTemperatureCelsius { get; init; }
    public GpuPowerEvidence? GpuPower { get; init; }

    public void RequireFresh(string expectedRequestId, DateTimeOffset now)
    {
        if (IsSimulated || RequestId != expectedRequestId || Source != ReviewedSource || Warnings is null ||
            CapturedAtUtc > now.AddSeconds(1) || now - CapturedAtUtc > TimeSpan.FromSeconds(8) ||
            CpuFanRpm is < 0 or > 12000 || GpuFanRpm is < 0 or > 12000 ||
            CpuTemperatureCelsius is <= 0 or > 125 || GpuTemperatureCelsius is <= 0 or > 125 ||
            ((CpuFanRpm is null || GpuFanRpm is null) && Warnings.Count == 0) ||
            (GpuPower is not null && (GpuPower.IsSimulated || GpuPower.Source != GpuPowerEvidence.WindowsSource ||
                GpuPower.DevicePowerState is < 1 or > 4 || GpuPower.CapturedAtUtc > now.AddSeconds(1) ||
                now - GpuPower.CapturedAtUtc > TimeSpan.FromSeconds(8))))
            throw new InvalidDataException("Fan reader result is stale, invalid or has unverified provenance.");
    }
}

namespace Gs66Control.Core;

/// <summary>A diagnostic snapshot; unsupported fields are deliberately absent rather than fabricated.</summary>
public sealed record HardwareSnapshot(
    string Source,
    bool IsSimulated,
    DateTimeOffset CapturedAtUtc,
    string OperatingSystem,
    string? Manufacturer,
    string? Model,
    string? Cpu,
    IReadOnlyList<string> Gpus,
    string? BiosVersion,
    IReadOnlyList<string> MsiWmiClasses,
    IReadOnlyList<string> Warnings);

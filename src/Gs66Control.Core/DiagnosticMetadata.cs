namespace Gs66Control.Core;

public sealed record EmbeddedControllerVersion(byte Major, byte Minor);

public static class DiagnosticMetadata
{
    public static bool IsMsiClass(string name) =>
        name.StartsWith("MSI_", StringComparison.OrdinalIgnoreCase);

    // SMBIOS uses 0xFF for an unknown EC release; it is not firmware version 255.
    public static EmbeddedControllerVersion? ParseEcVersion(string? major, string? minor) =>
        byte.TryParse(major, out var majorValue) && majorValue != byte.MaxValue &&
        byte.TryParse(minor, out var minorValue) && minorValue != byte.MaxValue
            ? new(majorValue, minorValue)
            : null;
}

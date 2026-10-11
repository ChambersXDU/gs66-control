using System.Text.Json;
using Gs66Control.Core;

namespace Gs66Control.App;

internal static class SnapshotExport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Export the complete shared diagnostic schema, including metadata. The probe
    // controls allowed fields and strips potentially identifying error messages.
    public static string Serialize(HardwareSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);
}

using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

if (args.Length != 1 || args[0] is not ("simulate" or "diagnose"))
{
    Console.Error.WriteLine("Usage: gs66-control simulate | diagnose");
    return 2;
}

if (args[0] == "diagnose" && !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Windows is required for real diagnostics.");
    return 2;
}

IHardwareProbe probe = args[0] == "simulate"
    ? new SimulatedHardwareProbe()
    : new WindowsWmiProbe();

try
{
    var snapshot = await probe.CaptureAsync();
    Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Diagnostic failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

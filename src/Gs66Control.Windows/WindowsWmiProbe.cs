using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

/// <summary>Read-only WMI discovery. This class does not access EC registers.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWmiProbe : IHardwareProbe
{
    public Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The WMI probe requires Windows.");
        }

        return Task.Run(() => Capture(cancellationToken), cancellationToken);
    }

    private static HardwareSnapshot Capture(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        cancellationToken.ThrowIfCancellationRequested();

        var manufacturer = First("root\\cimv2", "SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer", warnings);
        var model = First("root\\cimv2", "SELECT Model FROM Win32_ComputerSystem", "Model", warnings);
        var cpu = First("root\\cimv2", "SELECT Name FROM Win32_Processor", "Name", warnings);
        var gpus = Values("root\\cimv2", "SELECT Name FROM Win32_VideoController", "Name", warnings);
        var bios = First("root\\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion", warnings);
        cancellationToken.ThrowIfCancellationRequested();

        // Enumeration is metadata-only. The list is evidence of class visibility,
        // not proof that any given fan or EC control operation is supported.
        var msiClasses = Values("root\\WMI", "SELECT * FROM meta_class", "__CLASS", warnings)
            .Where(name => name.Contains("MSI", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (msiClasses.Length == 0)
        {
            warnings.Add("No MSI-named classes found under root\\WMI; this does not prove hardware control is unsupported.");
        }

        warnings.Add("WMI discovery only: EC firmware version, fan RPM and fan curve are not measured.");
        return new HardwareSnapshot(
            Source: "windows-wmi",
            IsSimulated: false,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            OperatingSystem: Environment.OSVersion.VersionString,
            Manufacturer: manufacturer,
            Model: model,
            Cpu: cpu,
            Gpus: gpus,
            BiosVersion: bios,
            MsiWmiClasses: msiClasses,
            Warnings: warnings);
    }

    private static string? First(string scope, string query, string property, List<string> warnings) =>
        Values(scope, query, property, warnings).FirstOrDefault();

    private static IReadOnlyList<string> Values(string scope, string query, string property, List<string> warnings)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();
            var values = new List<string>();

            foreach (ManagementBaseObject item in results)
            {
                var value = item[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            return values;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            warnings.Add($"Unable to query {scope} ({property}): {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }
}

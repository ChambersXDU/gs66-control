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

        var manufacturer = First("root\\cimv2", "SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer", warnings, cancellationToken);
        var model = First("root\\cimv2", "SELECT Model FROM Win32_ComputerSystem", "Model", warnings, cancellationToken);
        var cpu = First("root\\cimv2", "SELECT Name FROM Win32_Processor", "Name", warnings, cancellationToken);
        var gpus = Values("root\\cimv2", "SELECT Name FROM Win32_VideoController", "Name", warnings, cancellationToken);
        var bios = First("root\\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion", warnings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Enumeration is metadata-only. The list is evidence of class visibility,
        // not proof that any given fan or EC control operation is supported.
        var schemas = ReadMsiClassDefinitions(warnings, cancellationToken);
        var msiClasses = schemas.Select(schema => schema.Name).ToArray();

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
            Warnings: warnings)
        {
            MsiWmiSchemas = schemas
        };
    }

    private static string? First(string scope, string query, string property, List<string> warnings, CancellationToken cancellationToken) =>
        Values(scope, query, property, warnings, cancellationToken).FirstOrDefault();

    private static System.Management.EnumerationOptions QueryOptions() => new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        ReturnImmediately = true,
        Rewindable = false
    };

    private static IReadOnlyList<string> Values(string scope, string query, string property, List<string> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query, QueryOptions());
            using var results = searcher.Get();
            var values = new List<string>();

            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = item[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value.Trim());
                    }
                }
            }

            return values;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Exception messages can contain machine names or paths; do not export them.
            warnings.Add($"Unable to query {scope} ({property}): {ex.GetType().Name}.");
            return [];
        }
    }

    private static IReadOnlyList<MsiWmiClassSchema> ReadMsiClassDefinitions(List<string> warnings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var schemas = new List<MsiWmiClassSchema>();
        try
        {
            // meta_class reads definitions. Never enumerate MSI_* instances: on the
            // studied BIOS, WQAI(1) writes EC[0xCE] despite being a query method.
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM meta_class", QueryOptions());
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = item["__CLASS"]?.ToString();
                    if (name is null || !name.Contains("MSI", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Keep names even when a definition's qualifiers are inaccessible.
                    try
                    {
                        var guid = Qualifier(item.Qualifiers, "guid")?.ToString();
                        var properties = item.Properties.Cast<PropertyData>()
                            .Select(property => new WmiPropertySchema(property.Name, property.Type.ToString(),
                                property.IsArray, Qualifier(property.Qualifiers, "key") is true))
                            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                        var methods = item is ManagementClass definition
                            ? definition.Methods.Cast<MethodData>().Select(method => method.Name)
                                .OrderBy(method => method, StringComparer.OrdinalIgnoreCase).ToArray()
                            : [];
                        schemas.Add(new MsiWmiClassSchema(name, guid, properties, methods));
                    }
                    catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
                    {
                        schemas.Add(new MsiWmiClassSchema(name, null, [], []));
                        warnings.Add($"Unable to read class definition {name}: {ex.GetType().Name}.");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            warnings.Add($"Unable to query root\\WMI (class definitions): {ex.GetType().Name}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return schemas.DistinctBy(schema => schema.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(schema => schema.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static object? Qualifier(QualifierDataCollection qualifiers, string name) =>
        qualifiers.Cast<QualifierData>().FirstOrDefault(qualifier =>
            qualifier.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
}

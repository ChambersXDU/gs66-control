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
        var ecMajor = First("root\\cimv2", "SELECT EmbeddedControllerMajorVersion FROM Win32_BIOS", "EmbeddedControllerMajorVersion", warnings);
        var ecMinor = First("root\\cimv2", "SELECT EmbeddedControllerMinorVersion FROM Win32_BIOS", "EmbeddedControllerMinorVersion", warnings);
        var ecVersion = DiagnosticMetadata.ParseEcVersion(ecMajor, ecMinor);
        if (ecVersion is null)
        {
            warnings.Add("SMBIOS EC release is unavailable or unknown (255); this SMBIOS field does not identify the MSI EC firmware.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // Enumeration is metadata-only. The list is evidence of class visibility,
        // not proof that any given fan or EC control operation is supported.
        var msiClasses = Values("root\\WMI", "SELECT * FROM meta_class", "__CLASS", warnings)
            .Where(DiagnosticMetadata.IsMsiClass)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (msiClasses.Length == 0)
        {
            warnings.Add("No MSI-named classes found under root\\WMI; this does not prove hardware control is unsupported.");
        }

        var schemas = new List<WmiClassSchema>();
        foreach (var name in msiClasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schema = ReadSchema(name, warnings, cancellationToken);
            if (schema is not null)
            {
                schemas.Add(schema);
            }
        }

        MsiEcFirmwareInfo? msiFirmware = null;
        if (model == "GS66 Stealth 10SE" && bios == "E16V1IMS.10C")
        {
            try
            {
                var values = MsiWmiProperties.ReadIndexedBytes("MSI_Software", "Software", 34, cancellationToken);
                msiFirmware = MsiFirmwareParser.ParseSoftwareBytes(Enumerable.Range(0, 34).Select(index => values[index]).ToArray());
                if (msiFirmware is null) warnings.Add("MSI_Software firmware tag has an unrecognized format; no EC version inferred.");
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidDataException)
            { warnings.Add($"MSI_Software firmware tag unavailable: {ex.Message}"); }
        }

        warnings.Add("WMI discovery only: methods are metadata, never invoked. Fan RPM and fan curve are not measured; Firmware tags are reported by MSI_Software; they do not validate any control write operation.");
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
            EmbeddedControllerVersion = ecVersion,
            MsiWmiSchema = schemas,
            MsiEcFirmware = msiFirmware
        };
    }

    private static WmiClassSchema? ReadSchema(string name, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            using var definition = new ManagementClass("root\\WMI", name, new ObjectGetOptions
            {
                Timeout = TimeSpan.FromSeconds(15)
            });
            definition.Get();
            var properties = DescribeProperties(definition.Properties);
            var methods = new List<WmiMethodSchema>();
            foreach (MethodData method in definition.Methods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var input = method.InParameters;
                using var output = method.OutParameters;
                methods.Add(new WmiMethodSchema(method.Name,
                    input is null ? [] : DescribeProperties(input.Properties),
                    output is null ? [] : DescribeProperties(output.Properties)));
            }

            int? count = null;
            if (name is not ("MSI_Software" or "MSI_System" or "MSI_CPU" or "MSI_VGA" or "MSI_Power" or "MSI_Master_Battery" or "MSI_Slave_Battery"))
                return new WmiClassSchema(name, properties,
                    methods.OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase).ToArray(), null);
            try
            {
                using var instances = definition.GetInstances(new System.Management.EnumerationOptions
                {
                    Timeout = TimeSpan.FromSeconds(15)
                });
                var observed = 0;
                foreach (ManagementBaseObject instance in instances)
                {
                    using (instance)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        observed++;
                    }
                }
                count = observed;
                if (count == 0)
                {
                    warnings.Add($"root\\WMI:{name} has no visible instances; schema visibility does not establish hardware availability.");
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                warnings.Add($"Unable to enumerate instances of root\\WMI:{name}: {ex.GetType().Name}: {ex.Message}");
            }

            return new WmiClassSchema(name, properties,
                methods.OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase).ToArray(), count);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            warnings.Add($"Unable to read schema root\\WMI:{name}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<WmiPropertySchema> DescribeProperties(PropertyDataCollection properties) =>
        properties.Cast<PropertyData>()
            .Select(property => new WmiPropertySchema(property.Name, property.Type.ToString(), property.IsArray,
                property.Qualifiers.Cast<QualifierData>().Any(qualifier =>
                    qualifier.Name.Equals("Key", StringComparison.OrdinalIgnoreCase) && qualifier.Value is true)))
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? First(string scope, string query, string property, List<string> warnings) =>
        Values(scope, query, property, warnings).FirstOrDefault();

    private static IReadOnlyList<string> Values(string scope, string query, string property, List<string> warnings)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(15);
            using var results = searcher.Get();
            var values = new List<string>();

            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var value = item[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value.Trim());
                    }
                }
            }

            if (values.Count == 0)
            {
                warnings.Add($"No non-empty {property} values returned from {scope}: {query}.");
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

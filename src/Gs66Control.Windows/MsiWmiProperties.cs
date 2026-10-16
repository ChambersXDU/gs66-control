using System.Management;
using System.Runtime.Versioning;

namespace Gs66Control.Windows;

[SupportedOSPlatform("windows")]
internal static class MsiWmiProperties
{
    internal static byte ReadTemperatureByte(Gs66Control.Core.FanCurveChannel channel, CancellationToken token)
    {
        var (className, property) = channel switch
        {
            Gs66Control.Core.FanCurveChannel.Cpu => ("MSI_CPU", "CPU"),
            Gs66Control.Core.FanCurveChannel.Gpu => ("MSI_VGA", "VGA"),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        token.ThrowIfCancellationRequested();
        const string name = @"ACPI\PNP0C14\0_1";
        var path = className + ".InstanceName=\"" + name.Replace("\\", "\\\\") + "\"";
        using var row = new ManagementObject("root\\WMI", path, new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(3) });
        row.Get();
        if (row["InstanceName"]?.ToString() != name || row["Active"] is not true || row[property] is not byte value)
            throw new InvalidDataException("Reviewed temperature instance identity or shape changed.");
        return value;
    }

    internal static IReadOnlyDictionary<int, byte> ReadFanCounters(CancellationToken token)
    {
        using var definition = new ManagementClass("root\\WMI", "MSI_AP", new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(5) });
        definition.Get();
        if (!Guid.TryParse(definition.Qualifiers["guid"]?.Value?.ToString(), out var guid) ||
            guid != Guid.Parse("A1753D7C-B621-DE4A-B41A-55716A0ECE7A"))
            throw new InvalidDataException("MSI_AP firmware object binding changed.");
        var counters = new Dictionary<int, byte>();
        foreach (var index in new[] { 2, 3, 4, 5 })
        {
            token.ThrowIfCancellationRequested();
            var name = @"ACPI\PNP0C14\0_" + index;
            var path = "MSI_AP.InstanceName=\"" + name.Replace("\\", "\\\\") + "\"";
            using var row = new ManagementObject("root\\WMI", path, new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(5) });
            row.Get();
            if (row["InstanceName"]?.ToString() != name || row["Active"] is not true || row["AP"] is not byte value)
                throw new InvalidDataException("MSI_AP counter instance identity or shape changed.");
            counters.Add(index, value);
        }
        return counters;
    }

    internal static IReadOnlyDictionary<int, byte> ReadIndexedBytes(string className, string property, int expectedCount, CancellationToken token)
    {
        using var searcher = new ManagementObjectSearcher("root\\WMI", $"SELECT InstanceName, Active, {property} FROM {className}");
        searcher.Options.Timeout = TimeSpan.FromSeconds(5);
        using var rows = searcher.Get();
        var bytes = new Dictionary<int, byte>();
        string? instanceBase = null;
        var ordinal = 0;
        foreach (ManagementBaseObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var name = row["InstanceName"]?.ToString() ?? "";
                var split = name.LastIndexOf('_');
                if (split < 0 || !int.TryParse(name[(split + 1)..], out var index) ||
                    index != ordinal++ || row["Active"] is not true || row[property] is not byte value ||
                    (instanceBase is not null && instanceBase != name[..split]) || !bytes.TryAdd(index, value))
                    throw new InvalidDataException($"{className} instance identity, order or data shape has changed.");
                instanceBase = name[..split];
            }
        }
        if (bytes.Count != expectedCount || Enumerable.Range(0, expectedCount).Any(i => !bytes.ContainsKey(i)))
            throw new InvalidDataException($"{className} requires exactly {expectedCount} active indexed instances.");
        return bytes;
    }

}

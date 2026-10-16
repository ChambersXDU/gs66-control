using System.Management;
using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

/// <summary>
/// The only WMI writer exposed to the curve transaction. Its class names,
/// properties, instance counts and writable indexes are fixed at compile time.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RestrictedMsiPropertyWriter
{
    private const int CpuInstanceCount = 19;
    private const int GpuInstanceCount = 18;
    private const int SystemInstanceCount = 21;

    internal static byte ReadMode(CancellationToken token) =>
        MsiWmiProperties.ReadIndexedBytes("MSI_System", "System", SystemInstanceCount, token)[9];

    internal static IReadOnlyDictionary<int, byte> ReadSpeeds(FanCurveChannel channel, CancellationToken token)
    {
        var (className, property, count) = Channel(channel);
        var values = MsiWmiProperties.ReadIndexedBytes(className, property, count, token);
        return Enumerable.Range(0, LegacyFanCurveBinding.PointCount)
            .ToDictionary(index => index, index => values[LegacyFanCurveBinding.SpeedStartIndex + index]);
    }

    internal static void UpdateSpeed(
        FanCurveChannel channel,
        FanCurvePoint point,
        byte expected,
        byte target,
        CancellationToken token)
    {
        var (className, property, count) = Channel(channel);
        var index = LegacyFanCurveBinding.SpeedStartIndex + (int)point;
        UpdateByte(className, property, count, index, expected, target, token);
    }

    internal static void UpdateMode(byte expected, byte target, CancellationToken token)
    {
        if (!LegacyFanCurveBinding.IsReviewedMode(expected) ||
            !LegacyFanCurveBinding.IsReviewedMode(target))
            throw new InvalidOperationException("Only the reviewed automatic and custom curve modes may be written.");
        UpdateByte("MSI_System", "System", SystemInstanceCount, 9, expected, target, token);
    }

    private static void UpdateByte(
        string className,
        string property,
        int expectedCount,
        int targetIndex,
        byte expected,
        byte target,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = MsiWmiProperties.ReadIndexedBytes(className, property, expectedCount, token);
        if (!before.TryGetValue(targetIndex, out var beforeValue) || beforeValue != expected)
            throw new InvalidOperationException($"{className}[{targetIndex}] changed before the WMI update; refusing to overwrite unknown state.");
        if (beforeValue == target)
            return;

        using var searcher = new ManagementObjectSearcher("root\\WMI", $"SELECT * FROM {className}");
        searcher.Options.Timeout = TimeSpan.FromSeconds(5);
        using var collection = searcher.Get();
        var rows = ReadRows(collection, className, property, expectedCount, targetIndex, token);
        try
        {
            var selected = rows.Single(row => row.Index == targetIndex).Object;
            if (selected[property] is not byte current || current != expected)
                throw new InvalidOperationException($"{className}[{targetIndex}] changed before Put; refusing to overwrite unknown state.");

            selected[property] = target;
            selected.Put(new PutOptions { Type = PutType.UpdateOnly, Timeout = TimeSpan.FromSeconds(5) });
        }
        finally
        {
            foreach (var row in rows) row.Object.Dispose();
        }

        token.ThrowIfCancellationRequested();
        var after = MsiWmiProperties.ReadIndexedBytes(className, property, expectedCount, token);
        if (!after.TryGetValue(targetIndex, out var afterValue) || afterValue != target)
            throw new IOException($"{className}[{targetIndex}] did not match the requested value after Put.");
    }

    private static IReadOnlyList<(int Index, ManagementObject Object)> ReadRows(
        ManagementObjectCollection rows,
        string className,
        string property,
        int expectedCount,
        int targetIndex,
        CancellationToken token)
    {
        var objects = new List<(int Index, ManagementObject Object)>();
        string? instanceBase = null;
        var ordinal = 0;
        try
        {
            foreach (ManagementObject row in rows)
            {
                token.ThrowIfCancellationRequested();
                var name = row["InstanceName"]?.ToString() ?? "";
                var split = name.LastIndexOf('_');
                if (split < 0 || !int.TryParse(name[(split + 1)..], out var index) ||
                    index != ordinal++ || row["Active"] is not true || row[property] is not byte ||
                    (instanceBase is not null && instanceBase != name[..split]))
                {
                    row.Dispose();
                    throw new InvalidDataException($"{className} instance identity, order or data shape changed before Put.");
                }

                instanceBase = name[..split];
                objects.Add((index, row));
            }

            if (objects.Count != expectedCount || !objects.Any(row => row.Index == targetIndex))
                throw new InvalidDataException($"{className} requires the reviewed indexed instance layout before Put.");
            return objects;
        }
        catch
        {
            foreach (var row in objects) row.Object.Dispose();
            throw;
        }
    }

    private static (string ClassName, string Property, int InstanceCount) Channel(FanCurveChannel channel) =>
        channel switch
        {
            FanCurveChannel.Cpu => ("MSI_CPU", "CPU", CpuInstanceCount),
            FanCurveChannel.Gpu => ("MSI_VGA", "VGA", GpuInstanceCount),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
}

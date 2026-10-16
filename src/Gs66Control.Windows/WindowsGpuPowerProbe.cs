using System.Buffers.Binary;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsGpuPowerProbe
{
    private static readonly Lazy<string> Instance = new(() =>
    {
        using var search = new ManagementObjectSearcher("root\\cimv2",
            "SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE Name='NVIDIA GeForce RTX 2060'");
        search.Options.Timeout = TimeSpan.FromSeconds(3);
        using var results = search.Get();
        var ids = new List<string>();
        foreach (ManagementObject row in results)
            using (row)
                if (row["Name"] as string == "NVIDIA GeForce RTX 2060" && row["PNPDeviceID"] is string id &&
                    id.StartsWith("PCI\\VEN_10DE&", StringComparison.OrdinalIgnoreCase)) ids.Add(id);
        if (ids.Count != 1) throw new InvalidDataException("The reviewed GPU device instance is ambiguous or absent.");
        return ids[0];
    });

    public GpuPowerEvidence Capture()
    {
        if (CM_Locate_DevNodeW(out var node, Instance.Value, 0) != 0) throw new IOException("GPU device node is unavailable.");
        var key = new PropertyKey { Format = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 32 };
        var buffer = new byte[56]; uint length = (uint)buffer.Length;
        var result = CM_Get_DevNode_PropertyW(node, ref key, out var type, buffer, ref length, 0);
        if (result != 0 || type != 0x1003 || length != 56 || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != 56)
            throw new InvalidDataException("GPU CM_POWER_DATA is unavailable or its structure changed.");
        var state = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)));
        if (state is < 1 or > 4) throw new InvalidDataException("GPU power state is unknown.");
        return new(false, DateTimeOffset.UtcNow, state, GpuPowerEvidence.WindowsSource);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string instance, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type,
        [Out] byte[] buffer, ref uint size, uint flags);
}

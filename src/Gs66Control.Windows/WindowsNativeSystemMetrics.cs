using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Gs66Control.Windows;

/// <summary>
/// Cumulative processor counters returned by GetSystemTimes. Kernel already
/// includes idle time, so load is computed from deltas rather than sampled
/// with a blocking sleep.
/// </summary>
public readonly record struct NativeCpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    public bool TryGetTotal(out ulong total)
    {
        if (ulong.MaxValue - Kernel < User)
        {
            total = 0;
            return false;
        }
        total = Kernel + User;
        return Idle <= total;
    }
}

public static class WindowsNativeSystemMetricsMath
{
    /// <summary>
    /// Returns null until two valid samples exist or if counters regress. The
    /// method is pure so counter behavior can be tested without Windows APIs.
    /// </summary>
    public static double? ComputeCpuUsage(NativeCpuTimes? previous, NativeCpuTimes current)
    {
        if (previous is null || !previous.Value.TryGetTotal(out var previousTotal) ||
            !current.TryGetTotal(out var currentTotal)) return null;
        if (currentTotal < previousTotal || current.Idle < previous.Value.Idle) return null;

        var totalDelta = currentTotal - previousTotal;
        var idleDelta = current.Idle - previous.Value.Idle;
        if (totalDelta == 0 || idleDelta > totalDelta) return null;

        var busyDelta = totalDelta - idleDelta;
        return Math.Clamp(100d * busyDelta / totalDelta, 0d, 100d);
    }
}

/// <summary>
/// A single native Windows metrics snapshot. Nullable values represent an API
/// failure or an OS-reported unknown state; no WMI or synthetic value is used.
/// </summary>
public sealed record WindowsNativeSystemMetricsSnapshot(
    DateTimeOffset CapturedAtUtc,
    double? CpuUsagePercent,
    ulong? TotalMemoryBytes,
    ulong? AvailableMemoryBytes,
    bool? IsAcOnline,
    byte? BatteryPercent,
    bool? IsBatteryCharging,
    IReadOnlyList<string> Warnings)
{
    public double? MemoryUsagePercent =>
        TotalMemoryBytes is > 0 && AvailableMemoryBytes is not null
            ? Math.Clamp(100d * (TotalMemoryBytes.Value - Math.Min(TotalMemoryBytes.Value, AvailableMemoryBytes.Value)) /
                         TotalMemoryBytes.Value, 0d, 100d)
            : null;
}

/// <summary>
/// Lightweight Windows system metrics provider using documented kernel32
/// functions. Capture performs no sleeps and does not wake hardware.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNativeSystemMetrics
{
    private NativeCpuTimes? previousCpu;

    public WindowsNativeSystemMetricsSnapshot Capture()
    {
        var warnings = new List<string>();
        var capturedAt = DateTimeOffset.UtcNow;
        var currentCpu = ReadCpuTimes(warnings);
        var usage = currentCpu is null
            ? null
            : WindowsNativeSystemMetricsMath.ComputeCpuUsage(previousCpu, currentCpu.Value);
        previousCpu = currentCpu;

        var memory = ReadMemory(warnings);
        var power = ReadPower(warnings);
        return new(
            capturedAt,
            usage,
            memory.TotalBytes,
            memory.AvailableBytes,
            power.AcOnline,
            power.BatteryPercent,
            power.BatteryCharging,
            warnings);
    }

    private static NativeCpuTimes? ReadCpuTimes(List<string> warnings)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            warnings.Add($"GetSystemTimes failed with Win32 error {Marshal.GetLastWin32Error()}.");
            return null;
        }
        return new(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
    }

    private static (ulong? TotalBytes, ulong? AvailableBytes) ReadMemory(List<string> warnings)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            warnings.Add($"GlobalMemoryStatusEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
            return (null, null);
        }
        return (status.TotalPhysical, status.AvailablePhysical);
    }

    private static (bool? AcOnline, byte? BatteryPercent, bool? BatteryCharging) ReadPower(List<string> warnings)
    {
        if (!GetSystemPowerStatus(out var status))
        {
            warnings.Add($"GetSystemPowerStatus failed with Win32 error {Marshal.GetLastWin32Error()}.");
            return (null, null, null);
        }

        bool? acOnline = status.AcLineStatus switch
        {
            0 => false,
            1 => true,
            _ => null
        };
        var noBattery = (status.BatteryFlag & 0x80) != 0;
        byte? batteryPercent = noBattery || status.BatteryLifePercent == 255
            ? null
            : status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        bool? charging = noBattery || status.BatteryFlag == 255
            ? null
            : (status.BatteryFlag & 0x08) != 0;
        return (acOnline, batteryPercent, charging);
    }

    private static ulong ToUInt64(FileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

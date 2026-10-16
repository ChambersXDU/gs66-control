using System.Diagnostics;
using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsCurveRuntimeSession
{
    private readonly string source;
    internal WindowsCurveRuntimeSession(string verifiedSource) => source = verifiedSource;

    public FanCurveControlBaseline CaptureBaseline(CancellationToken token = default)
    {
        var timer = Stopwatch.StartNew();
        var mode = MsiWmiProperties.ReadIndexedBytes("MSI_System", "System", 21, token)[9];
        CurvePoint[] Read(string name, string property, int count)
        {
            var bytes = MsiWmiProperties.ReadIndexedBytes(name, property, count, token);
            return SavedFanCurves.ParseLegacyBytes(Enumerable.Range(0, count).Select(i => bytes[i]).ToArray(), count).ToArray();
        }
        var cpu = Read("MSI_CPU", "CPU", 19); var gpu = Read("MSI_VGA", "VGA", 18);
        if (!cpu.SequenceEqual(Read("MSI_CPU", "CPU", 19)) || !gpu.SequenceEqual(Read("MSI_VGA", "VGA", 18)) ||
            mode != MsiWmiProperties.ReadIndexedBytes("MSI_System", "System", 21, token)[9] || timer.Elapsed > TimeSpan.FromSeconds(4))
            throw new InvalidDataException("Runtime curve snapshot changed or exceeded its acquisition deadline.");
        var result = new FanCurveControlBaseline(false, DateTimeOffset.UtcNow, mode, cpu, gpu, source);
        FanCurveControlPolicy.Require(result, result.Cpu, result.Gpu, false, DateTimeOffset.UtcNow);
        return result;
    }

    public ThermalReading CaptureTemperatures(CancellationToken token = default)
    {
        var timer = Stopwatch.StartNew();
        var cpu = MsiTemperature.ToCelsius(MsiWmiProperties.ReadTemperatureByte(FanCurveChannel.Cpu, token));
        var gpu = MsiTemperature.ToCelsius(MsiWmiProperties.ReadTemperatureByte(FanCurveChannel.Gpu, token));
        GpuPowerEvidence? power = null;
        if (gpu is null)
        {
            try { power = new WindowsGpuPowerProbe().Capture(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        }
        if (timer.Elapsed > TimeSpan.FromSeconds(4)) throw new IOException("Runtime thermal acquisition exceeded its deadline.");
        return new(cpu, gpu, power);
    }
}

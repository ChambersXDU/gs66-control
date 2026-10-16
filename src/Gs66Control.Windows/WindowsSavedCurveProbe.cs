using System.Runtime.Versioning;
using Gs66Control.Core;

namespace Gs66Control.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsSavedCurveProbe
{
    public SavedFanCurves Capture(CancellationToken token)
    {
        var binding = new WindowsAutoFanBackend().Capture();
        AutoFanPolicy.Require(binding, false, false);
        var cpu = Read("MSI_CPU", "CPU", 19, token);
        var gpu = Read("MSI_VGA", "VGA", 18, token);
        if (!cpu.SequenceEqual(Read("MSI_CPU", "CPU", 19, token)) || !gpu.SequenceEqual(Read("MSI_VGA", "VGA", 18, token)))
            throw new InvalidDataException("Device curves changed during reading; retry without importing a mixed snapshot.");
        var mode = MsiWmiProperties.ReadIndexedBytes("MSI_System", "System", 21, token)[9];
        if (mode != binding.Mode) throw new InvalidDataException("Fan mode changed during curve reading.");
        AutoFanPolicy.Require(binding, false, false);
        return new(false, binding.CapturedAtUtc, mode, cpu, gpu,
            "MSI_CPU/MSI_VGA indices 4-9 and 11-16; reviewed Base Module legacy binding; saved values, not proof of active curve");
    }

    private static IReadOnlyList<CurvePoint> Read(string className, string property, int count, CancellationToken token)
    {
        var bytes = MsiWmiProperties.ReadIndexedBytes(className, property, count, token);
        return SavedFanCurves.ParseLegacyBytes(Enumerable.Range(0, count).Select(i => bytes[i]).ToArray(), count);
    }
}

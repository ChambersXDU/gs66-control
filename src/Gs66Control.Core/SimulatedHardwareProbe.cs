namespace Gs66Control.Core;

/// <summary>Safe fake data for local development on any OS. It is not a hardware reading.</summary>
public sealed class SimulatedHardwareProbe : IHardwareProbe
{
    public Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new HardwareSnapshot(
            Source: "simulation",
            IsSimulated: true,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            OperatingSystem: "Windows (simulated)",
            Manufacturer: "Micro-Star International Co., Ltd. (simulated)",
            Model: "GS66 Stealth 10SE (simulated)",
            Cpu: "Intel Core i7-10750H (simulated)",
            Gpus: ["NVIDIA GeForce RTX 2060 (simulated)"],
            BiosVersion: null,
            MsiWmiClasses: [],
            Warnings: ["Simulated data only. No real device or firmware has been queried."]));
    }
}

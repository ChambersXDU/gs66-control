using System.Text.Json;
using Gs66Control.Core;

namespace Gs66Control.Helper;

internal sealed record SimulationState(byte Mode = AutoFanPolicy.TrialBaseline, int? CpuTemperature = 50,
    int? GpuTemperature = 42, bool PermissionDenied = false, bool FailAfterWrite = false,
    bool RecoveryFails = false, int Writes = 0, bool IsSimulated = true);

internal sealed class SimulationBackend(string directory) : IAutoFanBackend
{
    public bool IsSimulated => true;
    internal SimulationState Read() => JsonSerializer.Deserialize<SimulationState>(File.ReadAllText(Path.Combine(directory, "state.json")))
        ?? throw new InvalidDataException("Simulation state is absent.");
    internal void Save(SimulationState state)
    {
        var temporary = Path.Combine(directory, $"state-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, Path.Combine(directory, "state.json"), true);
    }
    public AutoFanState Capture()
    {
        var state = Read();
        return new(state.IsSimulated, DateTimeOffset.UtcNow, "Micro-Star International Co., Ltd.", "GS66 Stealth 10SE",
            "E16V1IMS.10C", "16V1EMS1.109", "0311202011:16:49", true, true,
            state.Mode, state.CpuTemperature, state.GpuTemperature) { GpuTemperatureSource = "Simulation fixture" };
    }
    public void SetAutomatic(byte expectedMode)
    {
        var state = Read();
        AutoFanPolicy.Require(Capture(), true, expectedMode != AutoFanPolicy.Automatic);
        if (state.PermissionDenied) throw new UnauthorizedAccessException("Simulated write permission denied.");
        if (state.RecoveryFails && state.Writes > 0) throw new IOException("Simulated recovery failure.");
        if (state.Mode != expectedMode) throw new InvalidOperationException("Simulated concurrent change.");
        if (state.Mode == AutoFanPolicy.Automatic) return;
        Save(state with { Mode = AutoFanPolicy.Automatic, Writes = state.Writes + 1 });
        if (state.FailAfterWrite) throw new IOException("Simulated acknowledgement lost after write.");
    }
    public void RestoreTrialBaseline(byte expectedMode, byte baseline)
    {
        if (expectedMode != AutoFanPolicy.Automatic || baseline != AutoFanPolicy.TrialBaseline)
            throw new InvalidOperationException("Invalid simulated restore.");
        var state = Read();
        AutoFanPolicy.Require(Capture(), true);
        if (state.Mode != expectedMode) throw new InvalidOperationException("Simulated concurrent change.");
        if (state.PermissionDenied || state.RecoveryFails) throw new IOException("Simulated restore failure.");
        Save(state with { Mode = baseline, Writes = state.Writes + 1 });
    }
}

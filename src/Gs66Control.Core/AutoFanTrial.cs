namespace Gs66Control.Core;

public sealed record AutoFanState(
    bool IsSimulated, DateTimeOffset CapturedAtUtc, string Manufacturer, string Model,
    string BiosVersion, string EcVersion, string EcBuild, bool VendorArtifactMatches,
    bool SchemaMatches, byte Mode, int? CpuTemperature, int? GpuTemperature)
{
    public string GpuTemperatureSource { get; init; } = "MSI_VGA WMI";
    public GpuPowerEvidence? GpuPower { get; init; }
}

public static class NvidiaTemperature
{
    public static int? ParseSingleGs66Gpu(string output)
    {
        var rows = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length != 1) return null;
        var fields = rows[0].Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length != 2 || fields[0] != "NVIDIA GeForce RTX 2060" ||
            !int.TryParse(fields[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)) return null;
        return value is > 0 and <= 125 ? value : null;
    }
}

public sealed record AutoFanPreview(AutoFanState State, byte ProposedMode,
    bool Eligible, IReadOnlyList<string> Blockers, bool FirstHardwareWriteApproved = false,
    int HardwareWrites = 0);

public interface IAutoFanBackend
{
    bool IsSimulated { get; }
    AutoFanState Capture();
    void SetAutomatic(byte expectedMode);
    void RestoreTrialBaseline(byte expectedMode, byte baseline);
}

public static class AutoFanPolicy
{
    public const byte Automatic = 0x0D;
    public const byte TrialBaseline = 0x8D;
    public const int MaximumTrialSeconds = 15;
    public const string VendorArtifactSha256 = "cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192";

    public static IReadOnlyList<string> Blockers(AutoFanState state, bool simulation, DateTimeOffset now, bool requireTemperatures = true)
    {
        var errors = new List<string>();
        if (state.IsSimulated != simulation) errors.Add("Backend and evidence provenance do not match.");
        if (state.Manufacturer != "Micro-Star International Co., Ltd." || state.Model != "GS66 Stealth 10SE" ||
            state.BiosVersion != "E16V1IMS.10C" || state.EcVersion != "16V1EMS1.109" || state.EcBuild != "0311202011:16:49")
            errors.Add("Device/BIOS/EC is outside the experimental trial binding.");
        if (!state.VendorArtifactMatches) errors.Add("Installed vendor artifact does not match the reviewed implementation.");
        if (!state.SchemaMatches) errors.Add("MSI WMI instance schema is not verified.");
        var age = now - state.CapturedAtUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(10)) errors.Add("State is stale or its timestamp is invalid.");
        if (state.Mode is not (Automatic or TrialBaseline)) errors.Add("Fan mode is outside the two reviewed trial states.");
        // Conservative trial admission thresholds, not certified operating-temperature limits.
        if (requireTemperatures && (state.CpuTemperature is not (> 0 and <= 80) ||
            !new ThermalReading(state.CpuTemperature, state.GpuTemperature, state.GpuPower).GpuWithin(75, simulation, now)))
            errors.Add("Fresh safe CPU temperature and GPU temperature or verified current GPU D3 state are required.");
        return errors;
    }

    public static AutoFanPreview Preview(AutoFanState state, bool simulation, DateTimeOffset now)
    {
        var blockers = Blockers(state, simulation, now);
        return new(state, (byte)(state.Mode & 0x3F), blockers.Count == 0, blockers);
    }

    public static void Require(AutoFanState state, bool simulation, bool requireTemperatures = true)
    {
        var blockers = Blockers(state, simulation, DateTimeOffset.UtcNow, requireTemperatures);
        if (blockers.Count > 0) throw new InvalidOperationException(string.Join(" ", blockers));
    }
}

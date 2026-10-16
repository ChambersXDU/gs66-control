namespace Gs66Control.Core;

/// <summary>
/// Scenario labels used by the Dragon Center gaming UI for non-Prestige MSI notebooks.
/// The values are the persisted User Scenario Mode indices, not EC bytes.
/// </summary>
public enum Gs66PerformanceMode
{
    ExtremePerformance = 1,
    Balanced = 2,
    Silent = 3,
    SuperBattery = 4,
    Customize = 5
}

/// <summary>
/// The Shift enum values used by the reviewed Base Module before they are encoded
/// into the MSI_System:7 field.
/// </summary>
public enum Gs66ShiftType
{
    SportMode = 0,
    ComfortMode = 1,
    GreenMode = 2,
    Eco = 3,
    User = 4
}

/// <summary>
/// Pure interpretation of the Shift field. This model does not read or write WMI.
/// </summary>
public readonly record struct Gs66ShiftState(byte RawValue)
{
    public bool HasShiftSupport => (RawValue & 0x80) != 0;
    public bool IsActive => (RawValue & 0x40) != 0;

    public Gs66ShiftType? ShiftType => (RawValue & 0x3F) switch
    {
        0 => Gs66ShiftType.ComfortMode,
        1 => Gs66ShiftType.GreenMode,
        2 => Gs66ShiftType.Eco,
        3 => Gs66ShiftType.User,
        4 => Gs66ShiftType.SportMode,
        _ => null
    };

    public static byte Encode(Gs66ShiftType shiftType, byte existingValue = 0)
    {
        var value = (byte)(existingValue & 0xC3);
        value |= 0xC0;
        value = (byte)(value & 0xFC);
        value = shiftType switch
        {
            Gs66ShiftType.SportMode => (byte)(value + 4),
            Gs66ShiftType.ComfortMode => value,
            Gs66ShiftType.GreenMode => (byte)(value + 1),
            Gs66ShiftType.Eco => (byte)(value + 2),
            Gs66ShiftType.User => (byte)(value + 3),
            _ => throw new ArgumentOutOfRangeException(nameof(shiftType))
        };
        return value;
    }
}

/// <summary>
/// A scenario-to-Shift mapping recovered from the reviewed Dragon Center assembly.
/// Customize remains caller-selectable because its Performance registry value is user data.
/// </summary>
public sealed record Gs66PerformanceModeSpec(
    Gs66PerformanceMode Mode,
    Gs66ShiftType ShiftType,
    bool IsCustomizable,
    bool ChangesWindowsPowerPlan);

public static class Gs66PerformancePolicy
{
    /// <summary>
    /// The reviewed Base Module's built-in mapping for a gaming MSI notebook.
    /// The mapping intentionally does not claim PL1/PL2, GPU TGP, or fan behavior.
    /// </summary>
    public static Gs66PerformanceModeSpec Resolve(
        Gs66PerformanceMode mode,
        Gs66ShiftType customizeShift = Gs66ShiftType.ComfortMode)
    {
        return mode switch
        {
            Gs66PerformanceMode.ExtremePerformance =>
                new(mode, Gs66ShiftType.SportMode, false, false),
            Gs66PerformanceMode.Balanced =>
                new(mode, Gs66ShiftType.GreenMode, false, false),
            Gs66PerformanceMode.Silent =>
                new(mode, Gs66ShiftType.GreenMode, false, false),
            Gs66PerformanceMode.SuperBattery =>
                new(mode, Gs66ShiftType.Eco, false, false),
            Gs66PerformanceMode.Customize =>
                new(mode, customizeShift, true, false),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    public static bool IsValidCustomizeShift(Gs66ShiftType shiftType) =>
        Enum.IsDefined(shiftType);

    public static IReadOnlyList<string> Blockers(
        Gs66PerformanceModeSpec spec,
        Gs66ShiftState? currentState,
        bool deviceBindingVerified,
        bool legacyShiftPathAvailable,
        bool isSimulated)
    {
        var errors = new List<string>();
        if (!deviceBindingVerified) errors.Add("Device, BIOS and EC binding is not verified.");
        if (!legacyShiftPathAvailable) errors.Add("The MSI Shift control path is not verified on this machine.");
        if (currentState is null) errors.Add("Current Shift state is unavailable.");
        else if (!currentState.Value.HasShiftSupport || !currentState.Value.IsActive)
            errors.Add("Current Shift state does not advertise an active supported mode.");
        if (isSimulated) errors.Add("Simulated evidence cannot authorize a hardware mode change.");
        if (spec.Mode == Gs66PerformanceMode.Customize && !IsValidCustomizeShift(spec.ShiftType))
            errors.Add("Customize mode contains an unsupported Shift value.");
        return errors;
    }

    public static byte ProposedShiftValue(Gs66PerformanceModeSpec spec, byte currentRawValue) =>
        Gs66ShiftState.Encode(spec.ShiftType, currentRawValue);
}

/// <summary>
/// Safe planning data for the separate Windows power-plan path used by Dragon Center's
/// Game Creator mode. It is deliberately a plan, not a process launcher.
/// </summary>
public sealed record WindowsPowerPlanPlan(
    Guid PreviousPlan,
    Guid RequestedPlan,
    bool TargetIsAvailable,
    bool CanApply,
    IReadOnlyList<string> Blockers)
{
    public static readonly Guid DragonCenterHighPerformancePlan =
        new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static WindowsPowerPlanPlan Create(
        Guid? activePlan,
        IEnumerable<Guid>? availablePlans,
        bool requestHighPerformance)
    {
        var errors = new List<string>();
        var previous = activePlan ?? Guid.Empty;
        var available = availablePlans?.ToHashSet() ?? [];
        var target = requestHighPerformance ? DragonCenterHighPerformancePlan : previous;
        var targetAvailable = !requestHighPerformance || available.Contains(target);
        if (previous == Guid.Empty) errors.Add("The current Windows power plan is unavailable.");
        if (requestHighPerformance && !targetAvailable)
            errors.Add("The reviewed high-performance plan is not present.");
        return new(previous, target, targetAvailable, errors.Count == 0, errors);
    }
}

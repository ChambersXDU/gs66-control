namespace Gs66Control.Core;

/// <summary>
/// The legacy Base Module curve layout used by the reviewed GS66 package.
/// Temperature slots are device supplied; only the six speed slots per channel
/// are user-editable in the shipped UI.
/// </summary>
public static class LegacyFanCurveBinding
{
    public const int PointCount = 6;
    public const int LegacyInstanceCount = 19;
    public const int TemperatureStartIndex = 4;
    public const int SpeedStartIndex = 11;
    public const int MaximumWireSpeed = byte.MaxValue;
    public const int MaximumUiSpeedPercent = 100;

    public const byte ReviewedAutomaticMode = 0x0D;
    public const byte ReviewedCustomMode = 0x8D;
    public const byte CustomModeBit = 0x80;
    public const byte CoolerBoostBit = 0x40;
    public const int HighTemperatureThresholdCelsius = 80;
    public const int HighTemperatureMinimumSpeedPercent = 80;

    public static byte ActivateCustomMode(byte currentMode) =>
        (byte)((currentMode | CustomModeBit) & ~CoolerBoostBit);

    public static byte ActivateAutomaticMode(byte currentMode) =>
        (byte)(currentMode & 0x3F);

    public static bool IsReviewedMode(byte mode) =>
        mode is ReviewedAutomaticMode or ReviewedCustomMode;

    public static bool IsCustomMode(byte mode) =>
        mode == ReviewedCustomMode;
}

public enum FanCurveChannel
{
    Cpu,
    Gpu
}

public enum FanCurvePoint
{
    Point0 = 0,
    Point1 = 1,
    Point2 = 2,
    Point3 = 3,
    Point4 = 4,
    Point5 = 5
}

/// <summary>
/// A coherent read-only baseline captured immediately before a future curve
/// transaction. This model deliberately retains the device temperatures so a
/// caller cannot silently replace the firmware's thermal breakpoints.
/// </summary>
public sealed record FanCurveControlBaseline(
    bool IsSimulated,
    DateTimeOffset CapturedAtUtc,
    byte Mode,
    IReadOnlyList<CurvePoint> Cpu,
    IReadOnlyList<CurvePoint> Gpu,
    string Source)
{
    public static FanCurveControlBaseline FromSaved(SavedFanCurves curves) =>
        new(curves.IsSimulated, curves.CapturedAtUtc, curves.Mode, curves.Cpu, curves.Gpu, curves.Source);
}

/// <summary>
/// A staged curve change. It is a plan only: no hardware operation is implied
/// by constructing this value.
/// </summary>
public sealed record FanCurveControlPlan(
    FanCurveControlBaseline Baseline,
    IReadOnlyList<CurvePoint> TargetCpu,
    IReadOnlyList<CurvePoint> TargetGpu,
    byte TargetMode,
    bool RequiresRollbackJournal,
    bool RequiresIndependentRecovery,
    IReadOnlyList<string> Blockers)
{
    public bool Eligible => Blockers.Count == 0;
}

public static class FanCurveControlPolicy
{
    public static IReadOnlyList<string> Blockers(
        FanCurveControlBaseline baseline,
        IReadOnlyList<CurvePoint>? targetCpu,
        IReadOnlyList<CurvePoint>? targetGpu,
        bool simulation,
        DateTimeOffset now)
    {
        var errors = new List<string>();
        if (baseline.IsSimulated != simulation)
            errors.Add("Curve baseline and requested execution provenance do not match.");

        var age = now - baseline.CapturedAtUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(10))
            errors.Add("Curve baseline is stale or its timestamp is invalid.");

        if (!LegacyFanCurveBinding.IsReviewedMode(baseline.Mode))
            errors.Add("Fan mode is outside the reviewed automatic/custom curve states.");

        ValidateCurve(baseline.Cpu, "CPU baseline", errors);
        ValidateCurve(baseline.Gpu, "GPU baseline", errors);
        ValidateTarget(baseline.Cpu, targetCpu, "CPU target", errors);
        ValidateTarget(baseline.Gpu, targetGpu, "GPU target", errors);
        return errors;
    }

    public static FanCurveControlPlan Preview(
        FanCurveControlBaseline baseline,
        IReadOnlyList<CurvePoint> targetCpu,
        IReadOnlyList<CurvePoint> targetGpu,
        bool simulation,
        DateTimeOffset now)
    {
        var blockers = Blockers(baseline, targetCpu, targetGpu, simulation, now);
        return new(
            baseline,
            targetCpu,
            targetGpu,
            LegacyFanCurveBinding.ActivateCustomMode(baseline.Mode),
            RequiresRollbackJournal: true,
            RequiresIndependentRecovery: true,
            blockers);
    }

    public static void Require(
        FanCurveControlBaseline baseline,
        IReadOnlyList<CurvePoint> targetCpu,
        IReadOnlyList<CurvePoint> targetGpu,
        bool simulation,
        DateTimeOffset now)
    {
        var blockers = Blockers(baseline, targetCpu, targetGpu, simulation, now);
        if (blockers.Count > 0)
            throw new InvalidOperationException(string.Join(" ", blockers));
    }

    private static void ValidateTarget(
        IReadOnlyList<CurvePoint>? baseline,
        IReadOnlyList<CurvePoint>? target,
        string label,
        List<string> errors)
    {
        if (target is null || target.Count != LegacyFanCurveBinding.PointCount)
        {
            errors.Add($"{label} must contain exactly six points.");
            return;
        }

        if (baseline is null || baseline.Count != target.Count)
            return;

        for (var i = 0; i < target.Count; i++)
        {
            if (target[i] is null || target[i].Temperature != baseline[i].Temperature)
                errors.Add($"{label} temperature breakpoints are firmware supplied and cannot be changed.");
        }

        ValidateCurve(target, label, errors);
    }

    private static void ValidateCurve(
        IReadOnlyList<CurvePoint>? points,
        string label,
        List<string> errors)
    {
        if (points is null || points.Count != LegacyFanCurveBinding.PointCount)
        {
            errors.Add($"{label} must contain exactly six points.");
            return;
        }

        var previousTemperature = -1;
        var previousSpeed = -1;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point is null)
            {
                errors.Add($"{label} contains a missing point.");
                continue;
            }
            var firstZero = i == 0 && point.Temperature == 0;
            var validTemperature = firstZero || point.Temperature is >= 20 and <= 100;
            var validSpeed = point.Speed is >= 0 and <= LegacyFanCurveBinding.MaximumUiSpeedPercent;
            var increasing = point.Temperature > previousTemperature && point.Speed >= previousSpeed;
            if (!validTemperature || !validSpeed || !increasing)
                errors.Add($"{label} must have increasing temperatures, non-decreasing speeds, and 0–100 percent speeds.");

            if (point.Temperature >= LegacyFanCurveBinding.HighTemperatureThresholdCelsius &&
                point.Speed < LegacyFanCurveBinding.HighTemperatureMinimumSpeedPercent)
                errors.Add($"{label} must keep at least {LegacyFanCurveBinding.HighTemperatureMinimumSpeedPercent}% at {LegacyFanCurveBinding.HighTemperatureThresholdCelsius}°C and above.");

            previousTemperature = point.Temperature;
            previousSpeed = point.Speed;
        }
    }
}

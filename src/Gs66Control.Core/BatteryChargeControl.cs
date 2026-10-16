namespace Gs66Control.Core;

/// <summary>
/// The three charge limits observed in the reviewed Dragon Center battery UI.
/// The numeric values are the low seven bits of the WMI2 D7 data byte.
/// </summary>
public enum BatteryChargeLimit : byte
{
    BestForBattery = 60,
    Balanced = 80,
    BestForMobility = 100
}

/// <summary>
/// A normalized result from one read-only MSI_ACPI.Get_Data(0xD7) call.
/// The adapter must trim the provider's fixed WMI buffer to the one-byte
/// Get_Data payload before constructing this observation.
/// </summary>
public sealed record BatteryChargeGetDataObservation(
    bool IsHardware,
    bool IsD7GetDataProof,
    bool InvocationSucceeded,
    byte Flag,
    IReadOnlyList<byte>? Data,
    DateTimeOffset CapturedAtUtc,
    string Source);

/// <summary>
/// A charge-limit plan. Constructing or inspecting a plan performs no WMI or
/// hardware operation. A later Windows adapter must perform its own explicit
/// approval, write and immediate read-back checks.
/// </summary>
public sealed record BatteryChargeControlPlan(
    BatteryChargeLimit RequestedLimit,
    byte? CurrentRawValue,
    BatteryChargeLimit? CurrentLimit,
    byte? ProposedRawValue,
    bool RequiresImmediateReadback,
    bool RequiresIndependentRecovery,
    IReadOnlyList<string> Blockers)
{
    public bool Eligible => Blockers.Count == 0;
}

public static class BatteryChargePolicy
{
    public const byte D7Index = 0xD7;
    public const int GetDataMethodOrdinal = 24;
    public const int SetDataMethodOrdinal = 25;
    public const byte Bit7Mask = 0x80;
    public static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(10);

    public static BatteryChargeControlPlan Preview(
        BatteryChargeGetDataObservation? observation,
        BatteryChargeLimit requestedLimit,
        DateTimeOffset now)
    {
        var blockers = new List<string>();
        byte? currentRaw = null;
        BatteryChargeLimit? currentLimit = null;
        byte? proposedRaw = null;

        if (!IsSupportedLimit(requestedLimit))
            blockers.Add("Requested battery limit is outside the reviewed 60/80/100 percent presets.");

        if (observation is null)
        {
            blockers.Add("A fresh hardware MSI_ACPI.Get_Data(0xD7) observation is required.");
        }
        else
        {
            if (!observation.IsHardware)
                blockers.Add("Simulated or non-hardware battery evidence cannot authorize a charge plan.");

            if (!observation.IsD7GetDataProof)
                blockers.Add("The observation is not proof of MSI_ACPI.Get_Data(0xD7).");

            if (!observation.InvocationSucceeded)
                blockers.Add("The MSI_ACPI.Get_Data(0xD7) invocation did not complete successfully.");

            if (observation.Flag != 1)
                blockers.Add("MSI_ACPI.Get_Data(0xD7) did not report a successful flag.");

            if (observation.Data is null || observation.Data.Count != 1)
            {
                blockers.Add("MSI_ACPI.Get_Data(0xD7) must provide exactly one normalized payload byte.");
            }
            else
            {
                currentRaw = observation.Data[0];
                currentLimit = ParseLimit((byte)(currentRaw.Value & ~Bit7Mask));
                if (currentLimit is null)
                {
                    blockers.Add("The current D7 low-seven-bit limit is unknown; no target byte will be prepared.");
                }
                else if (IsSupportedLimit(requestedLimit))
                {
                    proposedRaw = (byte)((currentRaw.Value & Bit7Mask) | (byte)requestedLimit);
                }
            }

            var age = now - observation.CapturedAtUtc;
            if (age < TimeSpan.Zero || age > MaximumObservationAge)
                blockers.Add("The D7 observation is stale or has an invalid timestamp.");

            if (string.IsNullOrWhiteSpace(observation.Source))
                blockers.Add("The D7 observation has no provider source description.");
        }

        return new(
            requestedLimit,
            currentRaw,
            currentLimit,
            proposedRaw,
            RequiresImmediateReadback: true,
            RequiresIndependentRecovery: true,
            blockers);
    }

    public static bool IsSupportedLimit(BatteryChargeLimit limit) =>
        limit is BatteryChargeLimit.BestForBattery or
            BatteryChargeLimit.Balanced or
            BatteryChargeLimit.BestForMobility;

    public static BatteryChargeLimit? ParseLimit(byte lowSevenBits) => lowSevenBits switch
    {
        (byte)BatteryChargeLimit.BestForBattery => BatteryChargeLimit.BestForBattery,
        (byte)BatteryChargeLimit.Balanced => BatteryChargeLimit.Balanced,
        (byte)BatteryChargeLimit.BestForMobility => BatteryChargeLimit.BestForMobility,
        _ => null
    };

    public static bool TryEncodeTarget(
        byte currentRawValue,
        BatteryChargeLimit requestedLimit,
        out byte targetRawValue)
    {
        targetRawValue = 0;
        if (!IsSupportedLimit(requestedLimit) ||
            ParseLimit((byte)(currentRawValue & ~Bit7Mask)) is null)
            return false;

        targetRawValue = (byte)((currentRawValue & Bit7Mask) | (byte)requestedLimit);
        return true;
    }
}

using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using Gs66Control.Core;

namespace Gs66Control.Windows;

/// <summary>
/// The four Shift bytes reviewed for the verified GS66 package. The names are
/// deliberately neutral: Dragon Center's scenario labels and the firmware's
/// labels do not form a one-to-one mapping.
/// </summary>
public enum Gs66ReviewedShiftValue : byte
{
    C0 = 0xC0,
    C1 = 0xC1,
    C2 = 0xC2,
    C4 = 0xC4
}

/// <summary>
/// A human scenario label resolved to one reviewed raw Shift byte. Balanced
/// and Silent intentionally share C1; the UI labels are not claimed to be
/// distinct hardware settings.
/// </summary>
public sealed record WindowsPerformanceTarget(
    Gs66PerformanceMode Scenario,
    Gs66ReviewedShiftValue RawShift,
    bool SharesRawShiftWithAnotherScenario,
    string MappingNote);

/// <summary>A fresh, read-only MSI_System[7] snapshot with the exact control binding.</summary>
public sealed record WindowsPerformanceSnapshot(
    bool IsSimulated,
    DateTimeOffset CapturedAtUtc,
    string Manufacturer,
    string Model,
    string BiosVersion,
    string EcVersion,
    string EcBuild,
    bool VendorArtifactMatches,
    bool SchemaMatches,
    int SystemIndex,
    byte RawShiftValue)
{
    public Gs66ReviewedShiftValue? ReviewedShift =>
        Enum.IsDefined(typeof(Gs66ReviewedShiftValue), RawShiftValue)
            ? (Gs66ReviewedShiftValue)RawShiftValue
            : null;
}

/// <summary>
/// A no-write plan. Apply re-captures the binding and compares
/// ExpectedCurrentRawValue immediately before the WMI UpdateOnly operation.
/// </summary>
public sealed record WindowsPerformancePreview(
    WindowsPerformanceSnapshot Snapshot,
    WindowsPerformanceTarget? Target,
    byte ExpectedCurrentRawValue,
    bool IsRestore,
    IReadOnlyList<string> Blockers)
{
    public bool CanApply => Blockers.Count == 0;
}

public sealed record WindowsPerformanceApplyResult(
    WindowsPerformancePreview Preview,
    WindowsPerformanceSnapshot Before,
    WindowsPerformanceSnapshot After,
    bool HardwareWritePerformed);

/// <summary>
/// Windows-only Shift backend for the verified GS66 Stealth 10SE binding.
/// Reads are safe to call for diagnostics. Apply is intentionally explicit,
/// per-action elevated, compare-before-update and immediately read back.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPerformanceBackend
{
    public const int ShiftSystemIndex = 7;
    public const int ExpectedSystemInstanceCount = 21;
    public const int ExpectedSoftwareInstanceCount = 34;
    public const int WmiTimeoutSeconds = 5;
    public const int MaximumPreviewAgeSeconds = 10;

    public const string ExpectedManufacturer = "Micro-Star International Co., Ltd.";
    public const string ExpectedModel = "GS66 Stealth 10SE";
    public const string ExpectedBios = "E16V1IMS.10C";
    public const string ExpectedEcVersion = "16V1EMS1.109";
    public const string ExpectedEcBuild = "0311202011:16:49";
    public const string ReviewedBaseModuleSha256 =
        "cd5d9cb9fcee03fdc31589c45aaf96b5f35fa5c173c62bd6d4433a717c70e192";

    private static readonly byte[] ReviewedShiftBytes = [0xC0, 0xC1, 0xC2, 0xC4];

    /// <summary>
    /// Resolve a UI scenario without conflating its name with the raw byte.
    /// Customize must explicitly choose one of the reviewed bytes; C3 is not
    /// accepted because it is not in the reviewed stock set.
    /// </summary>
    public static WindowsPerformanceTarget ResolveTarget(
        Gs66PerformanceMode scenario,
        Gs66ReviewedShiftValue? customizeValue = null)
    {
        return scenario switch
        {
            Gs66PerformanceMode.ExtremePerformance => new(
                scenario, Gs66ReviewedShiftValue.C4, false,
                "Scenario label maps to reviewed raw Shift byte C4; firmware naming remains separate."),
            Gs66PerformanceMode.Balanced => new(
                scenario, Gs66ReviewedShiftValue.C1, true,
                "Balanced maps to C1. The reviewed scenario table shares this raw Shift value with Silent."),
            Gs66PerformanceMode.Silent => new(
                scenario, Gs66ReviewedShiftValue.C1, true,
                "Silent maps to C1. The reviewed scenario table shares this raw Shift value with Balanced."),
            Gs66PerformanceMode.SuperBattery => new(
                scenario, Gs66ReviewedShiftValue.C2, false,
                "Scenario label maps to reviewed raw Shift byte C2; battery policy is not inferred here."),
            Gs66PerformanceMode.Customize when customizeValue is not null &&
                IsReviewedShiftByte((byte)customizeValue.Value) => new(
                scenario, customizeValue.Value, false,
                "Customize accepts only an explicitly selected reviewed raw Shift byte."),
            Gs66PerformanceMode.Customize =>
                throw new ArgumentOutOfRangeException(nameof(customizeValue),
                    "Customize accepts only reviewed raw Shift bytes C0, C1, C2 and C4."),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    /// <summary>Read the current binding and MSI_System ordinal 7; never writes.</summary>
    public WindowsPerformanceSnapshot CaptureCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.UtcNow;
        var (manufacturer, model) = ComputerIdentity();
        var bios = SingleValue("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");

        var schemaMatches = true;
        RequireSchema("MSI_Software", "Software");
        RequireSchema("MSI_System", "System");
        RequireSchema("MSI_CPU", "CPU");
        RequireSchema("MSI_VGA", "VGA");

        var software = MsiWmiProperties.ReadIndexedBytes(
            "MSI_Software", "Software", ExpectedSoftwareInstanceCount, cancellationToken);
        var firmware = MsiFirmwareParser.ParseSoftwareBytes(
            Enumerable.Range(0, ExpectedSoftwareInstanceCount).Select(index => software[index]).ToArray())
            ?? throw new InvalidDataException("MSI_Software firmware bytes could not be decoded.");

        var system = MsiWmiProperties.ReadIndexedBytes(
            "MSI_System", "System", ExpectedSystemInstanceCount, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var artifactPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "MSI", "One Dragon Center", "Base Module", "API_NB_Base Module.dll");
        var artifactMatches = MatchesReviewedArtifact(artifactPath);

        return new(
            IsSimulated: false,
            CapturedAtUtc: capturedAt,
            Manufacturer: manufacturer,
            Model: model,
            BiosVersion: bios,
            EcVersion: firmware.Version,
            EcBuild: firmware.BuildText,
            VendorArtifactMatches: artifactMatches,
            SchemaMatches: schemaMatches,
            SystemIndex: ShiftSystemIndex,
            RawShiftValue: system[ShiftSystemIndex]);
    }

    /// <summary>Create a no-write plan for a human scenario.</summary>
    public WindowsPerformancePreview Preview(
        Gs66PerformanceMode scenario,
        Gs66ReviewedShiftValue? customizeValue = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = CaptureCurrent(cancellationToken);
        var target = ResolveTarget(scenario, customizeValue);
        return BuildPreview(snapshot, target, snapshot.RawShiftValue, isRestore: false);
    }

    /// <summary>
    /// Create a no-write restoration plan. The caller supplies the value that
    /// must still be present when Apply is reached, preventing stale restores.
    /// </summary>
    public WindowsPerformancePreview PreviewRestore(
        WindowsPerformanceSnapshot original,
        byte expectedCurrentRawValue)
    {
        var blockers = new List<string>();
        AddBindingBlockers(original, blockers, includeSnapshotAge: false);
        if (!IsReviewedShiftByte(original.RawShiftValue))
            blockers.Add("The original Shift byte is outside the reviewed stock set.");
        if (!IsReviewedShiftByte(expectedCurrentRawValue))
            blockers.Add("The expected current Shift byte is outside the reviewed stock set.");
        return new(
            original,
            Target: IsReviewedShiftByte(original.RawShiftValue)
                ? new WindowsPerformanceTarget(
                    Gs66PerformanceMode.Customize,
                    (Gs66ReviewedShiftValue)original.RawShiftValue,
                    false,
                    "Restoration target captured from a previously verified reviewed Shift byte.")
                : null,
            ExpectedCurrentRawValue: expectedCurrentRawValue,
            IsRestore: true,
            Blockers: blockers);
    }

    /// <summary>
    /// Apply a previously reviewed plan with an elevated process. Every call
    /// performs fresh identity/schema checks, expected-value comparison,
    /// bounded UpdateOnly, and immediate independent read-back.
    /// </summary>
    public WindowsPerformanceApplyResult Apply(
        WindowsPerformancePreview preview,
        CancellationToken cancellationToken = default)
    {
        if (!preview.CanApply)
            throw new InvalidOperationException(string.Join(" ", preview.Blockers));
        if (preview.Target is null || !IsReviewedShiftByte((byte)preview.Target.RawShift))
            throw new InvalidOperationException("No reviewed Shift target is available.");
        if (preview.Snapshot.IsSimulated)
            throw new InvalidOperationException("Simulated evidence cannot authorize a hardware update.");
        if (!preview.IsRestore &&
            DateTimeOffset.UtcNow - preview.Snapshot.CapturedAtUtc > TimeSpan.FromSeconds(MaximumPreviewAgeSeconds))
            throw new InvalidOperationException("The performance preview is stale; capture a new preview.");

        EnsureElevatedForAction();
        cancellationToken.ThrowIfCancellationRequested();
        var before = CaptureCurrent(cancellationToken);
        EnsureSameBinding(preview.Snapshot, before);
        if (before.RawShiftValue != preview.ExpectedCurrentRawValue)
            throw new InvalidOperationException("Shift value changed concurrently; update refused.");

        var target = (byte)preview.Target.RawShift;
        RequireFreshTemperatureGateForIncrease(before.RawShiftValue, target);
        var hardwareWrite = before.RawShiftValue != target;
        if (hardwareWrite)
            UpdateOnlySystemIndex7(before.RawShiftValue, target, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var after = CaptureCurrent(cancellationToken);
        if (after.RawShiftValue != target)
            throw new IOException($"MSI_System[{ShiftSystemIndex}] read-back was 0x{after.RawShiftValue:X2}, expected 0x{target:X2}.");
        return new(preview, before, after, hardwareWrite);
    }

    private static WindowsPerformancePreview BuildPreview(
        WindowsPerformanceSnapshot snapshot,
        WindowsPerformanceTarget target,
        byte expectedCurrentRawValue,
        bool isRestore)
    {
        var blockers = new List<string>();
        AddBindingBlockers(snapshot, blockers, includeSnapshotAge: true);
        if (!IsReviewedShiftByte((byte)target.RawShift))
            blockers.Add("The target Shift byte is outside the reviewed stock set.");
        return new(snapshot, target, expectedCurrentRawValue, isRestore, blockers);
    }

    private static void AddBindingBlockers(
        WindowsPerformanceSnapshot snapshot,
        List<string> blockers,
        bool includeSnapshotAge)
    {
        if (snapshot.IsSimulated) blockers.Add("Simulated evidence cannot authorize a hardware update.");
        if (snapshot.Manufacturer != ExpectedManufacturer || snapshot.Model != ExpectedModel ||
            snapshot.BiosVersion != ExpectedBios || snapshot.EcVersion != ExpectedEcVersion ||
            snapshot.EcBuild != ExpectedEcBuild)
            blockers.Add("Device, BIOS or EC binding is outside the reviewed GS66 control target.");
        if (!snapshot.VendorArtifactMatches)
            blockers.Add("Installed Base Module artifact does not match the reviewed hash.");
        if (!snapshot.SchemaMatches || snapshot.SystemIndex != ShiftSystemIndex)
            blockers.Add("MSI_System schema or control index is not verified.");
        if (!IsReviewedShiftByte(snapshot.RawShiftValue))
            blockers.Add("Current Shift byte is outside the reviewed stock set.");
        if (includeSnapshotAge)
        {
            var age = DateTimeOffset.UtcNow - snapshot.CapturedAtUtc;
            if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(MaximumPreviewAgeSeconds))
                blockers.Add("Performance snapshot is stale or has an invalid timestamp.");
        }
    }

    private static void RequireFreshTemperatureGateForIncrease(byte currentRawValue, byte targetRawValue)
    {
        if (ShiftPriority(targetRawValue) <= ShiftPriority(currentRawValue)) return;

        // A performance increase must have fresh CPU/GPU admission data. This
        // deliberately reuses the reviewed, read-only fan temperature binding;
        // lowering a mode does not wake the discrete GPU just to pass a gate.
        var thermal = new WindowsAutoFanBackend().Capture();
        AutoFanPolicy.Require(thermal, simulation: false, requireTemperatures: true);
        if (thermal.Mode == LegacyFanCurveBinding.ReviewedCustomMode)
        {
            var curves = new WindowsCurveControlBackend().CaptureBaseline();
            FanCurveControlPolicy.Require(curves, curves.Cpu, curves.Gpu, false, DateTimeOffset.UtcNow);
        }
    }

    private static int ShiftPriority(byte rawValue) => rawValue switch
    {
        0xC2 => 0,
        0xC1 => 1,
        0xC0 => 2,
        0xC4 => 3,
        _ => -1
    };

    private static bool IsReviewedShiftByte(byte value) => ReviewedShiftBytes.Contains(value);

    private static void EnsureSameBinding(WindowsPerformanceSnapshot expected, WindowsPerformanceSnapshot actual)
    {
        if (expected.IsSimulated != actual.IsSimulated ||
            expected.Manufacturer != actual.Manufacturer || expected.Model != actual.Model ||
            expected.BiosVersion != actual.BiosVersion || expected.EcVersion != actual.EcVersion ||
            expected.EcBuild != actual.EcBuild ||
            expected.VendorArtifactMatches != actual.VendorArtifactMatches ||
            expected.SchemaMatches != actual.SchemaMatches || expected.SystemIndex != actual.SystemIndex)
            throw new InvalidOperationException("Device or control binding changed since preview; update refused.");
    }

    private static void EnsureElevatedForAction()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Performance mode changes require explicit per-action elevation.");
    }

    private static void UpdateOnlySystemIndex7(
        byte expectedCurrentRawValue,
        byte targetRawValue,
        CancellationToken cancellationToken)
    {
        if (!IsReviewedShiftByte(targetRawValue))
            throw new ArgumentOutOfRangeException(nameof(targetRawValue), "Only reviewed stock Shift bytes are accepted.");

        using var searcher = new ManagementObjectSearcher(
            "root\\WMI", "SELECT * FROM MSI_System");
        searcher.Options.Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds);
        using var rows = searcher.Get();
        var objects = new List<ManagementObject>();
        ManagementObject? selected = null;
        try
        {
            foreach (ManagementObject row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                objects.Add(row);
            }

            if (objects.Count != ExpectedSystemInstanceCount)
                throw new InvalidDataException("MSI_System instance count changed.");

            string? instanceBase = null;
            for (var ordinal = 0; ordinal < objects.Count; ordinal++)
            {
                var row = objects[ordinal];
                var name = row["InstanceName"] as string ?? "";
                var split = name.LastIndexOf('_');
                if (split < 0 || name[(split + 1)..] != ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    row["Active"] is not true || row["System"] is not byte ||
                    (instanceBase is not null && instanceBase != name[..split]))
                    throw new InvalidDataException("MSI_System instance binding changed.");
                instanceBase = name[..split];
                if (ordinal == ShiftSystemIndex) selected = row;
            }

            if (selected is null || selected["System"] is not byte current || current != expectedCurrentRawValue)
                throw new InvalidOperationException("Shift value changed concurrently; update refused.");

            selected["System"] = targetRawValue;
            selected.Put(new PutOptions
            {
                Type = PutType.UpdateOnly,
                Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds)
            });
        }
        finally
        {
            foreach (var row in objects) row.Dispose();
        }

        var readBack = MsiWmiProperties.ReadIndexedBytes(
            "MSI_System", "System", ExpectedSystemInstanceCount, cancellationToken)[ShiftSystemIndex];
        if (readBack != targetRawValue)
            throw new IOException($"MSI_System[{ShiftSystemIndex}] read-back was 0x{readBack:X2}, expected 0x{targetRawValue:X2}.");
    }

    private static bool MatchesReviewedArtifact(string path)
    {
        if (!File.Exists(path)) return false;
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).Equals(
            ReviewedBaseModuleSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Manufacturer, string Model) ComputerIdentity()
    {
        using var query = new ManagementObjectSearcher(
            "root\\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        query.Options.Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds);
        using var results = query.Get();
        if (results.Count != 1) throw new InvalidDataException("Expected one computer identity.");
        foreach (ManagementBaseObject row in results)
            using (row) return (row["Manufacturer"]?.ToString() ?? "", row["Model"]?.ToString() ?? "");
        throw new InvalidDataException("Computer identity unavailable.");
    }

    private static string SingleValue(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher("root\\cimv2", query);
        searcher.Options.Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds);
        using var results = searcher.Get();
        if (results.Count != 1) throw new InvalidDataException("Expected one BIOS identity.");
        foreach (ManagementBaseObject row in results)
            using (row) return row[property]?.ToString() ?? "";
        throw new InvalidDataException("BIOS identity unavailable.");
    }

    private static void RequireSchema(string className, string valueProperty)
    {
        using var schema = new ManagementClass("root\\WMI", className, null);
        schema.Options.Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds);
        schema.Get();
        var value = schema.Properties[valueProperty];
        var key = schema.Properties["InstanceName"];
        var active = schema.Properties["Active"];
        if (value is null || key is null || active is null ||
            value.Type != CimType.UInt8 || value.IsArray ||
            key.Type != CimType.String || key.IsArray ||
            key.Qualifiers["Key"]?.Value is not true ||
            active.Type != CimType.Boolean || active.IsArray)
            throw new InvalidDataException($"{className} property/key schema changed.");
    }
}

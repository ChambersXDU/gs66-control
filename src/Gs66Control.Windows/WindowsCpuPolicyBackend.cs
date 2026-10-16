using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Gs66Control.Windows;

public enum CpuPolicySetting { EnergyPreference, BoostMode, MaximumFrequency, MaximumState, MinimumState, AutonomousMode }

public sealed record CpuPolicyValues(int EnergyPreference, int BoostMode, int MaximumFrequencyMhz, int MaximumStatePercent)
{
    public void ValidateTarget()
    {
        if (EnergyPreference is < 0 or > 100 || BoostMode is < 0 or > 6 || MaximumStatePercent is < 50 or > 100 ||
            (MaximumFrequencyMhz != 0 && MaximumFrequencyMhz is < 800 or > 5000))
            throw new InvalidDataException("CPU policy is outside the reviewed Windows setting ranges.");
    }

    internal int Value(CpuPolicySetting setting) => setting switch
    {
        CpuPolicySetting.EnergyPreference => EnergyPreference,
        CpuPolicySetting.BoostMode => BoostMode,
        CpuPolicySetting.MaximumFrequency => MaximumFrequencyMhz,
        CpuPolicySetting.MaximumState => MaximumStatePercent,
        _ => throw new InvalidOperationException("This setting is read-only.")
    };
}

public sealed record CpuPolicyOverlaySnapshot(Guid Scheme, int EnergyPreference);

public sealed record CpuPolicySnapshot(Guid Scheme, CpuPolicyValues Ac, CpuPolicyValues Dc,
    int MinimumAcState, int MinimumDcState, int AutonomousAc, int AutonomousDc,
    DateTimeOffset CapturedAtUtc, bool IsSimulated)
{
    public CpuPolicyOverlaySnapshot? AcOverlay { get; init; }
    public CpuPolicyOverlaySnapshot? DcOverlay { get; init; }
    public int EffectiveEnergyPreference(bool ac) => ac
        ? AcOverlay?.EnergyPreference ?? Ac.EnergyPreference
        : DcOverlay?.EnergyPreference ?? Dc.EnergyPreference;
}

public interface ICpuPolicyNativeAdapter
{
    bool IsSimulated { get; }
    Guid ActiveScheme();
    int Read(Guid scheme, CpuPolicySetting setting, bool ac);
    void Write(Guid scheme, CpuPolicySetting setting, bool ac, int value);
    void Activate(Guid scheme);
    Guid? OverlayScheme(bool ac) => null;
}

public sealed class WindowsCpuPolicyBackend(ICpuPolicyNativeAdapter native)
{
    private static readonly CpuPolicySetting[] Writable = [CpuPolicySetting.EnergyPreference, CpuPolicySetting.BoostMode,
        CpuPolicySetting.MaximumFrequency, CpuPolicySetting.MaximumState];

    [SupportedOSPlatform("windows")]
    public WindowsCpuPolicyBackend() : this(new PowrProfCpuPolicyAdapter()) { }

    public CpuPolicySnapshot Capture()
    {
        var scheme = native.ActiveScheme();
        if (scheme == Guid.Empty) throw new InvalidDataException("The active power plan is missing.");
        CpuPolicyValues Read(bool ac) => new(native.Read(scheme, CpuPolicySetting.EnergyPreference, ac),
            native.Read(scheme, CpuPolicySetting.BoostMode, ac), native.Read(scheme, CpuPolicySetting.MaximumFrequency, ac),
            native.Read(scheme, CpuPolicySetting.MaximumState, ac));
        var acOverlay = native.OverlayScheme(true);
        var dcOverlay = native.OverlayScheme(false);
        CpuPolicyOverlaySnapshot? Overlay(Guid? id, bool ac)
        {
            if (id is null || id == Guid.Empty) return null;
            var epp = native.Read(id.Value, CpuPolicySetting.EnergyPreference, ac);
            if (epp is < 0 or > 100) throw new InvalidDataException("Unsupported power-mode energy preference.");
            return new(id.Value, epp);
        }
        var snapshot = new CpuPolicySnapshot(scheme, Read(true), Read(false),
            native.Read(scheme, CpuPolicySetting.MinimumState, true), native.Read(scheme, CpuPolicySetting.MinimumState, false),
            native.Read(scheme, CpuPolicySetting.AutonomousMode, true), native.Read(scheme, CpuPolicySetting.AutonomousMode, false),
            DateTimeOffset.UtcNow, native.IsSimulated)
        { AcOverlay = Overlay(acOverlay, true), DcOverlay = Overlay(dcOverlay, false) };
        foreach (var value in new[] { snapshot.Ac, snapshot.Dc })
            if (value.EnergyPreference is < 0 or > 100 || value.BoostMode is < 0 or > 6 ||
                value.MaximumStatePercent is < 0 or > 100 || value.MaximumFrequencyMhz is < 0 or > 64000)
                throw new InvalidDataException("Unsupported CPU power-policy values were returned.");
        if (snapshot.MinimumAcState is < 0 or > 100 || snapshot.MinimumDcState is < 0 or > 100 ||
            snapshot.AutonomousAc is < 0 or > 1 || snapshot.AutonomousDc is < 0 or > 1 || native.ActiveScheme() != scheme ||
            native.OverlayScheme(true) != acOverlay || native.OverlayScheme(false) != dcOverlay)
            throw new InvalidDataException("CPU power-policy context changed or is unsupported.");
        return snapshot;
    }

    public CpuPolicySnapshot Apply(CpuPolicySnapshot expected, CpuPolicyValues target, bool ac, int? overlayEnergyPreference = null)
    {
        target.ValidateTarget();
        if (overlayEnergyPreference is < 0 or > 100)
            throw new InvalidDataException("Power-mode energy preference must be 0–100.");
        var age = DateTimeOffset.UtcNow - expected.CapturedAtUtc;
        if (age < -TimeSpan.FromSeconds(1) || age > TimeSpan.FromSeconds(10) || expected.IsSimulated != native.IsSimulated)
            throw new InvalidDataException("CPU power-policy snapshot is stale or its provenance differs.");
        var live = Capture();
        if (!SamePolicy(expected, live)) throw new InvalidOperationException("CPU power policy changed; refresh before applying.");
        if (target.MaximumStatePercent < (ac ? live.MinimumAcState : live.MinimumDcState))
            throw new InvalidOperationException("The maximum processor state is below the existing minimum.");
        var original = ac ? live.Ac : live.Dc;
        var originalOverlay = ac ? live.AcOverlay : live.DcOverlay;
        if (overlayEnergyPreference is not null && originalOverlay is null)
            throw new InvalidOperationException("The original power-mode override is no longer available.");
        var overlayTarget = overlayEnergyPreference ?? target.EnergyPreference;
        var mutations = new List<(Guid Scheme, CpuPolicySetting Setting, int Before, int After, bool Overlay)>();
        CpuPolicySnapshot Owned()
        {
            var owned = original;
            var overlay = originalOverlay;
            foreach (var mutation in mutations)
                if (mutation.Overlay) overlay = overlay! with { EnergyPreference = mutation.After };
                else owned = WithValue(owned, mutation.Setting, mutation.After);
            return ac ? live with { Ac = owned, AcOverlay = overlay } : live with { Dc = owned, DcOverlay = overlay };
        }
        void Write(Guid scheme, CpuPolicySetting setting, int before, int after, bool overlay)
        {
            if (before == after) return;
            if (!SamePolicy(Owned(), Capture())) throw new InvalidOperationException("CPU policy changed during the update.");
            // Journal ownership before calling a provider that might update and then lose its acknowledgement.
            mutations.Add((scheme, setting, before, after, overlay));
            native.Write(scheme, setting, ac, after);
            if (native.Read(scheme, setting, ac) != after) throw new IOException("CPU policy write could not be read back.");
        }
        try
        {
            foreach (var setting in Writable)
            {
                var before = original.Value(setting); var after = target.Value(setting);
                Write(live.Scheme, setting, before, after, false);
            }
            if (originalOverlay is not null)
                Write(originalOverlay.Scheme, CpuPolicySetting.EnergyPreference, originalOverlay.EnergyPreference, overlayTarget, true);
            if (mutations.Count != 0)
            {
                if (!SamePolicy(Owned(), Capture())) throw new InvalidOperationException("Power plan or mode changed before refresh.");
                native.Activate(live.Scheme);
            }
            var result = Capture();
            var requestedOverlay = originalOverlay is null ? null : originalOverlay with { EnergyPreference = overlayTarget };
            var requested = ac ? live with { Ac = target, AcOverlay = requestedOverlay } : live with { Dc = target, DcOverlay = requestedOverlay };
            if (!SamePolicy(requested, result)) throw new IOException("CPU policy final read-back differs from the request.");
            return result;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception>();
            var restoredAny = false;
            foreach (var mutation in mutations.AsEnumerable().Reverse())
            {
                try
                {
                    var value = native.Read(mutation.Scheme, mutation.Setting, ac);
                    if (value == mutation.After)
                    {
                        restoredAny = true;
                        native.Write(mutation.Scheme, mutation.Setting, ac, mutation.Before);
                    }
                    else if (value != mutation.Before) throw new IOException("An external CPU policy change was preserved.");
                    if (native.Read(mutation.Scheme, mutation.Setting, ac) != mutation.Before) throw new IOException("CPU policy rollback is unconfirmed.");
                }
                catch (Exception recovery) { errors.Add(recovery); }
            }
            try
            {
                if (restoredAny && native.ActiveScheme() == live.Scheme &&
                    native.OverlayScheme(ac) == originalOverlay?.Scheme) native.Activate(live.Scheme);
            }
            catch (Exception recovery) { errors.Add(recovery); }
            if (errors.Count != 0) throw new AggregateException("CPU policy update failed; recovery needs review.", new[] { failure }.Concat(errors));
            throw;
        }
    }

    public CpuPolicySnapshot Restore(CpuPolicySnapshot previous, CpuPolicySnapshot expectedCurrent, bool ac)
    {
        if (previous.Scheme != expectedCurrent.Scheme || previous.IsSimulated != expectedCurrent.IsSimulated ||
            previous.AcOverlay?.Scheme != expectedCurrent.AcOverlay?.Scheme || previous.DcOverlay?.Scheme != expectedCurrent.DcOverlay?.Scheme)
            throw new InvalidOperationException("Power plan or mode changed; the older configuration cannot be restored here.");
        var fresh = Capture();
        if (!SamePolicy(expectedCurrent, fresh)) throw new InvalidOperationException("CPU policy was changed elsewhere; restoration was not applied.");
        return Apply(fresh, ac ? previous.Ac : previous.Dc, ac,
            (ac ? previous.AcOverlay : previous.DcOverlay)?.EnergyPreference);
    }

    public static bool SamePolicy(CpuPolicySnapshot left, CpuPolicySnapshot right) =>
        left.Scheme == right.Scheme && left.Ac == right.Ac && left.Dc == right.Dc && left.MinimumAcState == right.MinimumAcState &&
        left.MinimumDcState == right.MinimumDcState && left.AutonomousAc == right.AutonomousAc && left.AutonomousDc == right.AutonomousDc &&
        left.AcOverlay == right.AcOverlay && left.DcOverlay == right.DcOverlay &&
        left.IsSimulated == right.IsSimulated;

    private static CpuPolicyValues WithValue(CpuPolicyValues value, CpuPolicySetting setting, int target) => setting switch
    {
        CpuPolicySetting.EnergyPreference => value with { EnergyPreference = target },
        CpuPolicySetting.BoostMode => value with { BoostMode = target },
        CpuPolicySetting.MaximumFrequency => value with { MaximumFrequencyMhz = target },
        CpuPolicySetting.MaximumState => value with { MaximumStatePercent = target },
        _ => throw new InvalidOperationException()
    };
}

[SupportedOSPlatform("windows")]
internal sealed class PowrProfCpuPolicyAdapter : ICpuPolicyNativeAdapter
{
    private readonly PowrProfPowerPlanNativeAdapter plans = new();
    public bool IsSimulated => false;
    public Guid ActiveScheme() => plans.GetActiveScheme();
    public void Activate(Guid scheme) => plans.SetActiveScheme(scheme);
    public Guid? OverlayScheme(bool ac)
    {
        // The slider's per-source persisted choice is distinct from the active base plan.
        // Read only its identity here; all policy writes still use documented PowrProf APIs.
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
        var value = key?.GetValue(ac ? "ActiveOverlayAcPowerScheme" : "ActiveOverlayDcPowerScheme");
        if (value is null) return null;
        if (value is not string text || !Guid.TryParse(text, out var id))
            throw new InvalidDataException("Windows power-mode identity is invalid.");
        return id == Guid.Empty ? null : id;
    }
    private static Guid Subgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static Guid Setting(CpuPolicySetting setting) => new(setting switch
    {
        CpuPolicySetting.EnergyPreference => "36687f9e-e3a5-4dbf-b1dc-15eb381c6863",
        CpuPolicySetting.BoostMode => "be337238-0d82-4146-a960-4f3749d470c7",
        CpuPolicySetting.MaximumFrequency => "75b0ae3f-bce0-45a7-8c89-c9611c25e100",
        CpuPolicySetting.MaximumState => "bc5038f7-23e0-4960-96da-33abaf5935ec",
        CpuPolicySetting.MinimumState => "893dee8e-2bef-41e0-89c6-b55d0929964c",
        CpuPolicySetting.AutonomousMode => "8baa4a8a-14c6-4451-8e8b-14bdbd197537",
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    });
    public int Read(Guid scheme, CpuPolicySetting setting, bool ac)
    {
        var id = Setting(setting);
        uint value;
        var result = ac ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref Subgroup, ref id, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref Subgroup, ref id, out value);
        if (result != 0) throw new Win32Exception((int)result, "Windows could not read this CPU power setting.");
        return checked((int)value);
    }
    public void Write(Guid scheme, CpuPolicySetting setting, bool ac, int value)
    {
        if (setting is CpuPolicySetting.MinimumState or CpuPolicySetting.AutonomousMode) throw new InvalidOperationException("This setting is read-only.");
        var id = Setting(setting);
        var result = ac ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref Subgroup, ref id, checked((uint)value))
            : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref Subgroup, ref id, checked((uint)value));
        if (result != 0) throw new Win32Exception((int)result, "Windows could not update this CPU power setting.");
    }
    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
}

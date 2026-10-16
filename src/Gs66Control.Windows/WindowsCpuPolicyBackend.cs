using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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

public sealed record CpuPolicySnapshot(Guid Scheme, CpuPolicyValues Ac, CpuPolicyValues Dc,
    int MinimumAcState, int MinimumDcState, int AutonomousAc, int AutonomousDc,
    DateTimeOffset CapturedAtUtc, bool IsSimulated);

public interface ICpuPolicyNativeAdapter
{
    bool IsSimulated { get; }
    Guid ActiveScheme();
    int Read(Guid scheme, CpuPolicySetting setting, bool ac);
    void Write(Guid scheme, CpuPolicySetting setting, bool ac, int value);
    void Activate(Guid scheme);
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
        var snapshot = new CpuPolicySnapshot(scheme, Read(true), Read(false),
            native.Read(scheme, CpuPolicySetting.MinimumState, true), native.Read(scheme, CpuPolicySetting.MinimumState, false),
            native.Read(scheme, CpuPolicySetting.AutonomousMode, true), native.Read(scheme, CpuPolicySetting.AutonomousMode, false),
            DateTimeOffset.UtcNow, native.IsSimulated);
        foreach (var value in new[] { snapshot.Ac, snapshot.Dc })
            if (value.EnergyPreference is < 0 or > 100 || value.BoostMode is < 0 or > 6 ||
                value.MaximumStatePercent is < 0 or > 100 || value.MaximumFrequencyMhz is < 0 or > 64000)
                throw new InvalidDataException("Unsupported CPU power-policy values were returned.");
        if (snapshot.MinimumAcState is < 0 or > 100 || snapshot.MinimumDcState is < 0 or > 100 ||
            snapshot.AutonomousAc is < 0 or > 1 || snapshot.AutonomousDc is < 0 or > 1 || native.ActiveScheme() != scheme)
            throw new InvalidDataException("CPU power-policy context changed or is unsupported.");
        return snapshot;
    }

    public CpuPolicySnapshot Apply(CpuPolicySnapshot expected, CpuPolicyValues target, bool ac)
    {
        target.ValidateTarget();
        var age = DateTimeOffset.UtcNow - expected.CapturedAtUtc;
        if (age < -TimeSpan.FromSeconds(1) || age > TimeSpan.FromSeconds(10) || expected.IsSimulated != native.IsSimulated)
            throw new InvalidDataException("CPU power-policy snapshot is stale or its provenance differs.");
        var live = Capture();
        if (!SamePolicy(expected, live)) throw new InvalidOperationException("CPU power policy changed; refresh before applying.");
        if (target.MaximumStatePercent < (ac ? live.MinimumAcState : live.MinimumDcState))
            throw new InvalidOperationException("The maximum processor state is below the existing minimum.");
        var original = ac ? live.Ac : live.Dc;
        var mutations = new List<(CpuPolicySetting Setting, int Before, int After)>();
        try
        {
            foreach (var setting in Writable)
            {
                var before = original.Value(setting); var after = target.Value(setting);
                if (before == after) continue;
                var current = Capture();
                var owned = original;
                foreach (var mutation in mutations) owned = WithValue(owned, mutation.Setting, mutation.After);
                var required = ac ? live with { Ac = owned } : live with { Dc = owned };
                if (!SamePolicy(required, current)) throw new InvalidOperationException("CPU policy changed during the update.");
                // Track before attempting the write: a provider can update and then lose its acknowledgement.
                mutations.Add((setting, before, after));
                native.Write(live.Scheme, setting, ac, after);
                if (native.Read(live.Scheme, setting, ac) != after) throw new IOException("CPU policy write could not be read back.");
            }
            if (mutations.Count != 0)
            {
                if (native.ActiveScheme() != live.Scheme) throw new InvalidOperationException("Active plan changed before refresh.");
                native.Activate(live.Scheme);
            }
            var result = Capture();
            var requested = ac ? live with { Ac = target } : live with { Dc = target };
            if (!SamePolicy(requested, result)) throw new IOException("CPU policy final read-back differs from the request.");
            return result;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception>();
            foreach (var mutation in mutations.AsEnumerable().Reverse())
            {
                try
                {
                    var value = native.Read(live.Scheme, mutation.Setting, ac);
                    if (value == mutation.After) native.Write(live.Scheme, mutation.Setting, ac, mutation.Before);
                    else if (value != mutation.Before) throw new IOException("An external CPU policy change was preserved.");
                    if (native.Read(live.Scheme, mutation.Setting, ac) != mutation.Before) throw new IOException("CPU policy rollback is unconfirmed.");
                }
                catch (Exception recovery) { errors.Add(recovery); }
            }
            try { if (mutations.Count != 0 && native.ActiveScheme() == live.Scheme) native.Activate(live.Scheme); }
            catch (Exception recovery) { errors.Add(recovery); }
            if (errors.Count != 0) throw new AggregateException("CPU policy update failed; recovery needs review.", new[] { failure }.Concat(errors));
            throw;
        }
    }

    public static bool SamePolicy(CpuPolicySnapshot left, CpuPolicySnapshot right) =>
        left.Scheme == right.Scheme && left.Ac == right.Ac && left.Dc == right.Dc && left.MinimumAcState == right.MinimumAcState &&
        left.MinimumDcState == right.MinimumDcState && left.AutonomousAc == right.AutonomousAc && left.AutonomousDc == right.AutonomousDc &&
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

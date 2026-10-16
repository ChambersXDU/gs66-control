using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class CpuPolicyOverlayTests
{
    [Fact]
    public void CapturesTheSliderOverrideInsteadOfReportingBaseEppAsEffective()
    {
        var backend = new WindowsCpuPolicyBackend(new Fake());
        var state = backend.Capture();
        Assert.Equal(33, state.Ac.EnergyPreference);
        Assert.Equal(0, state.EffectiveEnergyPreference(true));
        Assert.Equal(50, state.EffectiveEnergyPreference(false));
    }

    [Fact]
    public void UpdatesBaseAndOverlayOnlyForTheRequestedSource()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var after = backend.Apply(before, new(65, 3, 2800, 100), true);
        Assert.Equal(65, after.Ac.EnergyPreference); Assert.Equal(65, after.EffectiveEnergyPreference(true));
        Assert.Equal(before.Dc, after.Dc); Assert.Equal(before.DcOverlay, after.DcOverlay);
        Assert.Equal(5, native.Writes); Assert.Equal(1, native.Activations);
    }

    [Fact]
    public void AppliesOverlayEvenWhenBaseAlreadyMatchesAndThenBecomesANoOp()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var after = backend.Apply(before, before.Ac, true);
        Assert.Equal(33, after.EffectiveEnergyPreference(true)); Assert.Equal(1, native.Writes);
        backend.Apply(after, after.Ac, true);
        Assert.Equal(1, native.Writes); Assert.Equal(1, native.Activations);
    }

    [Fact]
    public void RestoresDistinctOriginalBaseAndOverlayPreferences()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var applied = backend.Apply(before, new(65, 3, 2800, 100), true);
        var restored = backend.Restore(before, applied, true);
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, restored));
        Assert.Equal(33, restored.Ac.EnergyPreference); Assert.Equal(0, restored.AcOverlay!.EnergyPreference);
    }

    [Fact]
    public void LostOverlayAcknowledgementRestoresAllOwnedChanges()
    {
        var native = new Fake { FailOverlayAcknowledgement = true }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<IOException>(() => backend.Apply(before, new(65, 3, 2800, 100), true));
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, backend.Capture()));
    }

    [Fact]
    public void RejectsAChangedSliderBeforeWriting()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        native.ActiveOverlay = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => backend.Apply(before, new(65, 3, 2800, 100), true));
        Assert.Equal(0, native.Writes);
    }

    [Fact]
    public void UndoPreservesChangesMadeByAnotherProgram()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var applied = backend.Apply(before, new(65, 3, 2800, 100), true);
        native.Values[(native.Base, CpuPolicySetting.MaximumFrequency, true)] = 3000;
        var writes = native.Writes;
        Assert.Throws<InvalidOperationException>(() => backend.Restore(before, applied, true));
        Assert.Equal(writes, native.Writes); Assert.Equal(3000, backend.Capture().Ac.MaximumFrequencyMhz);
    }

    [Fact]
    public void ActivationFailureRollsBackOverlayAndBase()
    {
        var native = new Fake { FailFirstActivation = true }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<IOException>(() => backend.Apply(before, new(65, 3, 2800, 100), true));
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, backend.Capture()));
    }

    [Fact]
    public void PreservesAnExternalSliderChangeDuringBaseWrites()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        native.AfterFirstWrite = () => native.Values[(native.ActiveOverlay, CpuPolicySetting.EnergyPreference, true)] = 88;
        Assert.Throws<InvalidOperationException>(() => backend.Apply(before, new(65, 3, 2800, 100), true));
        Assert.Equal(before.Ac, backend.Capture().Ac);
        Assert.Equal(88, backend.Capture().EffectiveEnergyPreference(true));
    }

    [Fact]
    public void DoesNotReactivateTheOriginalModeAfterAnExternalSliderSwitch()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        native.AfterFirstWrite = () => native.ActiveOverlay = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => backend.Apply(before, new(65, 3, 2800, 100), true));
        Assert.Equal(before.Ac, backend.Capture().Ac); Assert.Equal(0, native.Activations);
        Assert.NotEqual(before.AcOverlay!.Scheme, native.ActiveOverlay);
    }

    [Fact]
    public void AccessDeniedBeforeAnyMutationDoesNotAttemptAnotherPrivilegedRefresh()
    {
        var native = new Fake { DenyWrites = true }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var failure = Assert.Throws<System.ComponentModel.Win32Exception>(() => backend.Apply(before, new(65, 3, 2800, 100), true));
        Assert.Equal(5, failure.NativeErrorCode); Assert.Equal(0, native.Activations);
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, backend.Capture()));
    }

    private sealed class Fake : ICpuPolicyNativeAdapter
    {
        public readonly Guid Base = Guid.NewGuid();
        public Guid ActiveOverlay = Guid.NewGuid();
        public readonly Dictionary<(Guid, CpuPolicySetting, bool), int> Values = [];
        public bool IsSimulated => true;
        public int Writes, Activations;
        public bool FailOverlayAcknowledgement, FailFirstActivation, DenyWrites;
        public Action? AfterFirstWrite;
        public Fake()
        {
            foreach(var ac in new[]{true,false})
            {
                Values[(Base,CpuPolicySetting.EnergyPreference,ac)]=ac?33:50;
                Values[(Base,CpuPolicySetting.BoostMode,ac)]=2;
                Values[(Base,CpuPolicySetting.MaximumFrequency,ac)]=0;
                Values[(Base,CpuPolicySetting.MaximumState,ac)]=99;
                Values[(Base,CpuPolicySetting.MinimumState,ac)]=5;
                Values[(Base,CpuPolicySetting.AutonomousMode,ac)]=1;
            }
        }
        public Guid ActiveScheme()=>Base;
        public Guid? OverlayScheme(bool ac)=>ac?ActiveOverlay:null;
        public int Read(Guid scheme,CpuPolicySetting setting,bool ac)=>Values.TryGetValue((scheme,setting,ac),out var value)?value:0;
        public void Write(Guid scheme,CpuPolicySetting setting,bool ac,int value)
        {
            if (DenyWrites) throw new System.ComponentModel.Win32Exception(5);
            Writes++;Values[(scheme,setting,ac)]=value;
            if (Writes == 1) AfterFirstWrite?.Invoke();
            if(scheme!=Base && FailOverlayAcknowledgement){FailOverlayAcknowledgement=false;throw new IOException("Lost overlay write acknowledgement");}
        }
        public void Activate(Guid scheme)
        {Activations++;if(FailFirstActivation && Activations==1)throw new IOException("Activation failed");}
    }
}

using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class WindowsCpuPolicyTests
{
    [Fact]
    public void FrequencyPolicyAppliesOnlyToTheChosenDcSourceAndCanReturnToAutomatic()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var result = backend.Apply(before, before.Dc with { MaximumFrequencyMhz = 3000 }, false);
        Assert.Equal(3000, result.Dc.MaximumFrequencyMhz); Assert.Equal(before.Ac, result.Ac);
        var restored = backend.Apply(result, before.Dc, false);
        Assert.Equal(0, restored.Dc.MaximumFrequencyMhz);
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, restored));
    }

    [Fact]
    public void ChangesOnlyRequestedPowerSourceAndKeepsFullPerformanceAvailable()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        var result = backend.Apply(before, before.Ac with { EnergyPreference = 55, BoostMode = 1, MaximumStatePercent = 100 }, true);
        Assert.Equal(55, result.Ac.EnergyPreference); Assert.Equal(100, result.Ac.MaximumStatePercent);
        Assert.Equal(before.Dc, result.Dc); Assert.Equal(1, native.Activations); Assert.True(result.IsSimulated);
    }

    [Fact]
    public void NoOpDoesNotWriteOrReactivate()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        backend.Apply(before, before.Ac, true);
        Assert.Equal(0, native.Writes); Assert.Equal(0, native.Activations);
    }

    [Fact]
    public void RejectsStaleAndMismatchedSnapshotsBeforeWriting()
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<InvalidDataException>(() => backend.Apply(before with { CapturedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-11) }, before.Ac, true));
        Assert.Throws<InvalidDataException>(() => backend.Apply(before with { IsSimulated = false }, before.Ac, true));
        native.Values[(CpuPolicySetting.EnergyPreference, true)] = 25;
        Assert.Throws<InvalidOperationException>(() => backend.Apply(before, before.Ac, true));
        Assert.Equal(0, native.Writes);
    }

    [Theory]
    [InlineData(-1, 1, 0, 100)]
    [InlineData(101, 1, 0, 100)]
    [InlineData(50, 7, 0, 100)]
    [InlineData(50, 1, 799, 100)]
    [InlineData(50, 1, 5001, 100)]
    [InlineData(50, 1, 0, 49)]
    public void RejectsUnreviewedTargets(int epp, int boost, int mhz, int state)
    {
        var native = new Fake(); var backend = new WindowsCpuPolicyBackend(native);
        Assert.Throws<InvalidDataException>(() => backend.Apply(backend.Capture(), new(epp, boost, mhz, state), true));
        Assert.Equal(0, native.Writes);
    }

    [Fact]
    public void RestoresAcknowledgementLostMutationAndEarlierChanges()
    {
        var native = new Fake { LoseWriteAcknowledgement = 2 }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<IOException>(() => backend.Apply(before, before.Ac with { EnergyPreference = 60, BoostMode = 1 }, true));
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, backend.Capture()));
    }

    [Fact]
    public void RestoresPolicyWhenActivationFails()
    {
        var native = new Fake { FailFirstActivation = true }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<IOException>(() => backend.Apply(before, before.Ac with { EnergyPreference = 60 }, true));
        Assert.True(WindowsCpuPolicyBackend.SamePolicy(before, backend.Capture()));
        Assert.Equal(2, native.Activations);
    }

    [Fact]
    public void PreservesExternalChangeAndReportsUnconfirmedRollback()
    {
        var native = new Fake { ChangeAfterWrite = true }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<AggregateException>(() => backend.Apply(before, before.Ac with { EnergyPreference = 60 }, true));
        Assert.Equal(77, backend.Capture().Ac.EnergyPreference);
    }

    [Fact]
    public void DoesNotSwitchBackAnExternallyChangedPowerPlan()
    {
        var native = new Fake { SwitchAfterWrite = true }; var backend = new WindowsCpuPolicyBackend(native); var before = backend.Capture();
        Assert.Throws<InvalidOperationException>(() => backend.Apply(before, before.Ac with { EnergyPreference = 60 }, true));
        Assert.NotEqual(before.Scheme, native.ActiveScheme()); Assert.Equal(0, native.Activations);
        Assert.Equal(before.Ac.EnergyPreference, native.Values[(CpuPolicySetting.EnergyPreference, true)]);
    }

    private sealed class Fake : ICpuPolicyNativeAdapter
    {
        public bool IsSimulated => true;
        public readonly Dictionary<(CpuPolicySetting, bool), int> Values = new();
        private Guid scheme = Guid.NewGuid();
        public int Writes; public int Activations; public int LoseWriteAcknowledgement;
        public bool FailFirstActivation; public bool ChangeAfterWrite; public bool SwitchAfterWrite;
        public Fake()
        {
            foreach (var ac in new[] { true, false })
            {
                Values[(CpuPolicySetting.EnergyPreference, ac)] = ac ? 33 : 50;
                Values[(CpuPolicySetting.BoostMode, ac)] = 2;
                Values[(CpuPolicySetting.MaximumFrequency, ac)] = 0;
                Values[(CpuPolicySetting.MaximumState, ac)] = 99;
                Values[(CpuPolicySetting.MinimumState, ac)] = 5;
                Values[(CpuPolicySetting.AutonomousMode, ac)] = 1;
            }
        }
        public Guid ActiveScheme() => scheme;
        public int Read(Guid ignored, CpuPolicySetting setting, bool ac) => Values[(setting, ac)];
        public void Write(Guid ignored, CpuPolicySetting setting, bool ac, int value)
        {
            Writes++; Values[(setting, ac)] = value;
            if (ChangeAfterWrite) Values[(setting, ac)] = 77;
            if (SwitchAfterWrite) scheme = Guid.NewGuid();
            if (Writes == LoseWriteAcknowledgement) throw new IOException("Lost write acknowledgement");
        }
        public void Activate(Guid requested)
        {
            Activations++; if (FailFirstActivation && Activations == 1) throw new IOException("Activation failed");
            scheme = requested;
        }
    }
}

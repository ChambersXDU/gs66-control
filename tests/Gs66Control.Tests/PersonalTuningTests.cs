using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class PersonalTuningTests
{
    [Fact]
    public void QuietPresetsKeepBoostAndFullProcessorStateAndDoNotSelectFirmwareModes()
    {
        Assert.Equal(3,PersonalTuningPreset.BuiltIns.Count);
        foreach(var preset in PersonalTuningPreset.BuiltIns)
        {
            Assert.Empty(preset.Validate());Assert.Equal(3,preset.Policy.BoostMode);Assert.Equal(100,preset.Policy.MaximumStatePercent);
        }
        Assert.True(PersonalTuningPreset.BuiltIns[0].Policy.MaximumFrequencyMhz<PersonalTuningPreset.BuiltIns[2].Policy.MaximumFrequencyMhz);
    }

    [Fact]
    public void DistinguishesSliderOverrideSavedVendorModeAndLiveFirmwareDrift()
    {
        var policy=new PersonalCpuPolicy(65,3,3000,100);
        var warnings=PersonalTuningHealth.Check(policy,0,policy,4,0xC2);
        Assert.Equal(4,warnings.Count);
        Assert.Contains(warnings,w=>w.Contains("滑块"));Assert.Contains(warnings,w=>w.Contains("微星仍保存"));
        Assert.Empty(PersonalTuningHealth.Check(policy,65,policy,2,0xC1));
    }

    [Fact]
    public void UnknownHardwareDoesNotBecomeALowPerformanceDiagnosis()
    {
        Assert.Empty(PersonalTuningHealth.Check(new(65,3,3000,100),65,null,null,null));
    }

    [Fact]
    public void ValidatesCustomPresetsWithoutChangingExistingFanProfiles()
    {
        var custom=new PersonalTuningPreset("mine","我的方案","",new(70,3,2700,100));
        var settings=ConsoleSettings.Default with{CpuPresets=[custom],LastCpuPresetId="mine"};
        Assert.Empty(settings.Validate());Assert.Empty(settings.Profiles);
        Assert.NotEmpty((settings with{CpuPresets=[custom with{Id="office"}]}).Validate());
        Assert.NotEmpty((settings with{CpuPresets=[custom with{Policy=new(101,3,2700,100)}]}).Validate());
    }

    [Fact]
    public void PersonalChoicesRoundTripAlongsideOlderSettings()
    {
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var store=new SettingsStore(Path.Combine(directory,"settings.json"));
            store.Save(ConsoleSettings.Default with{CpuPresets=[new("mine","我的方案","",new(70,3,2700,100))],LastCpuPresetId="mine"});
            var loaded=store.Load();Assert.Equal("mine",loaded.LastCpuPresetId);Assert.Equal(2700,Assert.Single(loaded.CpuPresets).Policy.MaximumFrequencyMhz);
            Assert.Throws<InvalidDataException>(()=>store.Save(loaded with{CpuPresets=[null!]}));
            Assert.Equal("mine",store.Load().LastCpuPresetId);
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}

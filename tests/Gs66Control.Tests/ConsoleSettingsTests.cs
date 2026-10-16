using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class ConsoleSettingsTests
{
    private static ConsoleProfile Profile(string name = "work") => new(name,
        [new(50, 30), new(60, 45), new(70, 60), new(80, 75), new(90, 90), new(95, 100)],
        [new(50, 30), new(60, 45), new(70, 60), new(80, 75), new(90, 90), new(95, 100)]);

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void RejectsAggressiveOrInvalidPolling(int interval) =>
        Assert.NotEmpty((ConsoleSettings.Default with { RefreshSeconds = interval }).Validate());

    [Fact]
    public void RejectsDuplicateNamesIgnoringCase() =>
        Assert.NotEmpty((ConsoleSettings.Default with { Profiles = [Profile("work"), Profile("WORK")] }).Validate());

    [Fact]
    public void RejectsDescendingTemperatureAndSpeed()
    {
        var invalid = Profile() with { Cpu = [new(50, 30), new(60, 45), new(55, 40), new(80, 75), new(90, 90), new(95, 100)] };
        Assert.NotEmpty((ConsoleSettings.Default with { Profiles = [invalid] }).Validate());
    }

    [Fact]
    public void RoundTripsPreferencesAndKeepsLastGoodFileOnInvalidSave()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(root, "settings.json"));
            var good = ConsoleSettings.Default with { Profiles = [Profile()], RefreshSeconds = 4 };
            store.Save(good);
            Assert.Throws<InvalidDataException>(() => store.Save(good with { RefreshSeconds = 1 }));
            var loaded = store.Load();
            Assert.Equal(4, loaded.RefreshSeconds);
            Assert.Equal("work", Assert.Single(loaded.Profiles).Name);
            Assert.Equal(100, loaded.Profiles[0].Cpu[5].Speed);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RejectsMalformedSettingsRatherThanSilentlyOverwriting()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "settings.json");
            File.WriteAllText(path, "{}");
            Assert.Throws<InvalidDataException>(() => new SettingsStore(path).Load());
            Assert.Equal("{}", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(226, 0, 2123)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, null)]
    [InlineData(128, 1, 1250)]
    public void ConvertsVendorCounterWithoutDivisionByZero(byte low, byte high, int? expected) =>
        Assert.Equal(expected, FanCounter.ToRpm(low, high));
    [Theory]
    [InlineData(0, null)]
    [InlineData(255, null)]
    [InlineData(130, null)]
    [InlineData(48, 48)]
    [InlineData(95, 95)]
    public void DoesNotPresentUnknownTemperatureAsZeroDegrees(byte value, int? expected) =>
        Assert.Equal(expected, MsiTemperature.ToCelsius(value));

    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC1)]
    [InlineData(0xC2)]
    [InlineData(0xC4)]
    public void AcceptsReviewedProfilePerformance(int raw)
    {
        var profile = Profile() with { PerformanceShift = (byte)raw };
        Assert.Empty((ConsoleSettings.Default with { Profiles = [profile] }).Validate());
    }

    [Fact]
    public void RejectsUnreviewedProfilePerformance() =>
        Assert.NotEmpty((ConsoleSettings.Default with { Profiles = [Profile() with { PerformanceShift = 0xC3 }] }).Validate());
}

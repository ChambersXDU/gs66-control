using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class WindowsPowerPlanBackendTests
{
    [Fact]
    public void ParsesNativeWindowsGuidBytes()
    {
        var expected = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

        var actual = WindowsPowerPlanGuidParser.Parse(expected.ToByteArray());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RejectsEmptySchemeList()
    {
        var adapter = new FakeAdapter();

        Assert.Throws<InvalidDataException>(() => new WindowsPowerPlanBackend(adapter).Capture());
    }

    [Fact]
    public void RejectsMoreThanTheBoundedSchemeCount()
    {
        var guids = Enumerable.Range(1, WindowsPowerPlanBackend.MaximumExistingSchemes + 1)
            .Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:D12}"))
            .ToArray();
        var adapter = new FakeAdapter(guids, guids[0]);

        Assert.Throws<InvalidDataException>(() => new WindowsPowerPlanBackend(adapter).Capture());
    }

    [Fact]
    public void RejectsInvalidFriendlyName()
    {
        var scheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var adapter = new FakeAdapter([scheme], scheme);
        adapter.Names[scheme] = "   ";

        Assert.Throws<InvalidDataException>(() => new WindowsPowerPlanBackend(adapter).Capture());
    }

    [Fact]
    public void RefusesWhenTheFreshCurrentSchemeDiffersFromExpected()
    {
        var current = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var target = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        var adapter = new FakeAdapter([current, target], target);

        Assert.Throws<InvalidOperationException>(() =>
            new WindowsPowerPlanBackend(adapter).Activate(target, current));
        Assert.Equal(0, adapter.SetActiveCalls);
    }

    [Fact]
    public void RejectsWriteReadbackMismatch()
    {
        var current = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var target = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        var adapter = new FakeAdapter([current, target], current)
        {
            KeepActiveSchemeAfterWrite = true
        };

        Assert.Throws<IOException>(() =>
            new WindowsPowerPlanBackend(adapter).Activate(target, current));
        Assert.Equal(1, adapter.SetActiveCalls);
    }

    [Fact]
    public void ActivatesExistingSchemeAndReportsRealAdapterState()
    {
        var current = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var target = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        var adapter = new FakeAdapter([current, target], current);

        var result = new WindowsPowerPlanBackend(adapter).Activate(target, current);

        Assert.True(result.PowerPlanChanged);
        Assert.Equal(current, result.Before.ActiveSchemeGuid);
        Assert.Equal(target, result.After.ActiveSchemeGuid);
        Assert.All(result.After.Plans, plan => Assert.False(plan.IsSimulated));
        Assert.Equal(1, adapter.SetActiveCalls);
    }

    private sealed class FakeAdapter : IWindowsPowerPlanNativeAdapter
    {
        public FakeAdapter(IReadOnlyList<Guid>? schemes = null, Guid? active = null)
        {
            SchemeGuids = schemes?.ToArray() ?? [];
            ActiveScheme = active ?? Guid.Empty;
            Names = SchemeGuids.ToDictionary(guid => guid, guid => $"Plan {guid:N}");
        }

        public bool IsSimulated => false;

        public Guid[] SchemeGuids { get; }

        public Dictionary<Guid, string> Names { get; }

        public Guid ActiveScheme { get; private set; }

        public bool KeepActiveSchemeAfterWrite { get; init; }

        public int SetActiveCalls { get; private set; }

        public IReadOnlyList<Guid> EnumerateSchemeGuids() => SchemeGuids.ToArray();

        public Guid GetActiveScheme() => ActiveScheme;

        public string ReadFriendlyName(Guid schemeGuid) => Names[schemeGuid];

        public void SetActiveScheme(Guid schemeGuid)
        {
            SetActiveCalls++;
            if (!KeepActiveSchemeAfterWrite)
                ActiveScheme = schemeGuid;
        }
    }
}

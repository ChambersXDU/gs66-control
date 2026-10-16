using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class FanTelemetrySessionTests
{
    private static readonly string Id = Guid.NewGuid().ToString("N");
    private static FanTelemetryFrame Frame(DateTimeOffset now) => new(Id, false, now, 2200, 0, FanTelemetryFrame.ReviewedSource, []);

    [Fact]
    public void ReaderAcceptsFreshMeasuredStoppedFanButRejectsSimulationWrongSessionAndSource()
    {
        var now = DateTimeOffset.UtcNow;
        Frame(now).RequireFresh(Id, now);
        Assert.Throws<InvalidDataException>(() => (Frame(now) with { IsSimulated = true }).RequireFresh(Id, now));
        Assert.Throws<InvalidDataException>(() => Frame(now).RequireFresh(Guid.NewGuid().ToString("N"), now));
        Assert.Throws<InvalidDataException>(() => (Frame(now) with { Source = "guess" }).RequireFresh(Id, now));
    }

    [Fact]
    public void ReaderRejectsStaleFutureAndImplausibleValuesAndPreservesUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDataException>(() => Frame(now.AddSeconds(-9)).RequireFresh(Id, now));
        Assert.Throws<InvalidDataException>(() => Frame(now.AddSeconds(2)).RequireFresh(Id, now));
        Assert.Throws<InvalidDataException>(() => (Frame(now) with { CpuFanRpm = 12001 }).RequireFresh(Id, now));
        Assert.Throws<InvalidDataException>(() => (Frame(now) with { CpuFanRpm = null }).RequireFresh(Id, now));
        var missing = Frame(now) with { CpuFanRpm = null, Warnings = ["Unavailable"] };
        missing.RequireFresh(Id, now);
        Assert.Null(missing.CpuFanRpm);
    }

    [Fact]
    public void ReaderRequestRejectsMissingParentAndPathLikeSession()
    {
        new FanTelemetryRequest(Id, 123, DateTimeOffset.UtcNow).Validate();
        Assert.Throws<InvalidDataException>(() => new FanTelemetryRequest("../reader", 123, DateTimeOffset.UtcNow).Validate());
        Assert.Throws<InvalidDataException>(() => new FanTelemetryRequest(Id, 0, DateTimeOffset.UtcNow).Validate());
        Assert.Throws<InvalidDataException>(() => new FanTelemetryRequest(Id, 123, default).Validate());
    }
}

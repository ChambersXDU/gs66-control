using Gs66Control.Core;
using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class WindowsControlReadPacketTests
{
    private static WindowsControlReadPacket Packet(string nonce, DateTimeOffset now)
    {
        CurvePoint[] points = [new(0, 35), new(55, 50), new(60, 60), new(65, 72), new(80, 80), new(90, 85)];
        var performance = new WindowsPerformanceSnapshot(false, now, "Micro-Star International Co., Ltd.",
            "GS66 Stealth 10SE", "E16V1IMS.10C", "16V1EMS1.109", "0311202011:16:49", true, true, 7, 193);
        var automatic = new AutoFanState(false, now, performance.Manufacturer, performance.Model, performance.BiosVersion,
            performance.EcVersion, performance.EcBuild, true, true, 141, 50, 42);
        return new(nonce, now, new(false, now, 141, points, points, "reviewed fixture"), performance, automatic, null);
    }

    [Fact]
    public void PacketRequiresFreshMatchingResponseAndPreservesReaderFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        Packet(nonce, now).RequireFresh(nonce, now);
        Assert.Throws<InvalidDataException>(() => Packet(nonce, now).RequireFresh("other", now));
        Assert.Throws<InvalidDataException>(() => Packet(nonce, now).RequireFresh(nonce, now.AddSeconds(9)));
        Assert.Throws<InvalidDataException>(() => (Packet(nonce, now) with { Error = "Read failed" }).RequireFresh(nonce, now));
    }

    [Fact]
    public void PacketRejectsUnverifiedBindingsAndSimulationBeforeUse()
    {
        var now = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var packet = Packet(nonce, now);
        Assert.Throws<InvalidDataException>(() => (packet with { Curves = packet.Curves! with { IsSimulated = true } }).RequireFresh(nonce, now));
        Assert.Throws<InvalidDataException>(() => (packet with { Performance = packet.Performance! with { VendorArtifactMatches = false } }).RequireFresh(nonce, now));
        Assert.Throws<InvalidDataException>(() => (packet with { Performance = packet.Performance! with { RawShiftValue = 195 } }).RequireFresh(nonce, now));
        Assert.Throws<InvalidOperationException>(() => (packet with { Automatic = packet.Automatic! with { EcVersion = "other" } }).RequireFresh(nonce, now));
    }
}

using Gs66Control.Core;

namespace Gs66Control.Windows;

public sealed record WindowsControlReadRequest(string Nonce, DateTimeOffset RequestedAtUtc);

public sealed record WindowsControlReadPacket(string Nonce, DateTimeOffset CompletedAtUtc,
    SavedFanCurves? Curves, WindowsPerformanceSnapshot? Performance, AutoFanState? Automatic, string? Error)
{
    public void RequireFresh(string nonce, DateTimeOffset now)
    {
        if (Nonce != nonce || CompletedAtUtc > now.AddSeconds(1) || now - CompletedAtUtc > TimeSpan.FromSeconds(8))
            throw new InvalidDataException("Control reader result is stale or belongs to another request.");
        if (Error is not null) throw new InvalidDataException(Error);
        if (Curves is null || Performance is null || Automatic is null || Curves.IsSimulated || Performance.IsSimulated || Automatic.IsSimulated ||
            string.IsNullOrWhiteSpace(Curves.Source) || !Performance.VendorArtifactMatches || !Performance.SchemaMatches ||
            Performance.Manufacturer != WindowsPerformanceBackend.ExpectedManufacturer || Performance.Model != WindowsPerformanceBackend.ExpectedModel ||
            Performance.BiosVersion != WindowsPerformanceBackend.ExpectedBios || Performance.EcVersion != WindowsPerformanceBackend.ExpectedEcVersion ||
            Performance.EcBuild != WindowsPerformanceBackend.ExpectedEcBuild || Performance.SystemIndex != 7 || Performance.ReviewedShift is null ||
            now - Performance.CapturedAtUtc > TimeSpan.FromSeconds(10) || Performance.CapturedAtUtc > now.AddSeconds(1))
            throw new InvalidDataException("Control reader binding is invalid.");
        FanCurveControlPolicy.Require(FanCurveControlBaseline.FromSaved(Curves), Curves.Cpu, Curves.Gpu, false, now);
        AutoFanPolicy.Require(Automatic, false, requireTemperatures: false);
    }
}

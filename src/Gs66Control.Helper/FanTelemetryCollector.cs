using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Helper;

internal static class FanTelemetryCollector
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static async Task<int> Run(string requestId)
    {
        if (!Guid.TryParseExact(requestId, "N", out _)) throw new InvalidDataException("Invalid reader session.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control", "fan-requests", requestId);
        var requestPath = Path.Combine(directory, "request.json");
        if (new FileInfo(requestPath).Length > 8192) throw new InvalidDataException("Reader request is too large.");
        var request = JsonSerializer.Deserialize<FanTelemetryRequest>(File.ReadAllText(requestPath))
            ?? throw new InvalidDataException("Reader request missing.");
        request.Validate();
        if (request.RequestId != requestId || !Healthy(request, directory)) return 2;
        var identity = await new WindowsWmiProbe().CaptureAsync();
        if (identity.IsSimulated || identity.Manufacturer != "Micro-Star International Co., Ltd." ||
            identity.Model != "GS66 Stealth 10SE" || identity.BiosVersion != "E16V1IMS.10C" ||
            identity.MsiEcFirmware?.Version != "16V1EMS1.109" || identity.MsiEcFirmware.BuildText != "0311202011:16:49")
            throw new InvalidDataException("Read-only fan session is outside the reviewed device binding.");
        using (var self = Process.GetCurrentProcess())
            File.WriteAllText(Path.Combine(directory, "reader.json"), JsonSerializer.Serialize(new
            { RequestId = requestId, ReaderPid = self.Id, StartedAtUtc = new DateTimeOffset(self.StartTime.ToUniversalTime()) }));
        string? lastNonce = null;
        while (Healthy(request, directory))
        {
            var frame = WindowsFanTelemetryProbe.Capture(requestId);
            frame.RequireFresh(requestId, DateTimeOffset.UtcNow);
            if (!Healthy(request, directory)) break;
            var temporary = Path.Combine(directory, "frame.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(frame));
            File.Move(temporary, Path.Combine(directory, "frame.json"), true);
            var queryPath = Path.Combine(directory, "control-request.json");
            if (File.Exists(queryPath) && new FileInfo(queryPath).Length <= 2048)
            {
                var query = JsonSerializer.Deserialize<WindowsControlReadRequest>(File.ReadAllText(queryPath));
                if (query is not null && query.Nonce != lastNonce && Guid.TryParseExact(query.Nonce, "N", out _) &&
                    query.RequestedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(1) && DateTimeOffset.UtcNow - query.RequestedAtUtc <= TimeSpan.FromSeconds(10))
                {
                    lastNonce = query.Nonce;
                    WindowsControlReadPacket packet;
                    try
                    {
                        var curves = new WindowsSavedCurveProbe().Capture(CancellationToken.None);
                        var performance = new WindowsPerformanceBackend().CaptureCurrent();
                        var automatic = new WindowsAutoFanBackend().Capture();
                        packet = new(query.Nonce, DateTimeOffset.UtcNow, curves, performance, automatic, null);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    { packet = new(query.Nonce, DateTimeOffset.UtcNow, null, null, null, ex.Message); }
                    var response = Path.Combine(directory, "control-result.tmp");
                    File.WriteAllText(response, JsonSerializer.Serialize(packet));
                    File.Move(response, Path.Combine(directory, "control-result.json"), true);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return 0;
    }

    private static bool Healthy(FanTelemetryRequest request, string directory)
    {
        try
        {
            using var parent = Process.GetProcessById(request.ParentPid);
            if (parent.HasExited || (parent.StartTime.ToUniversalTime() - request.ParentStartUtc).Duration() > TimeSpan.FromSeconds(5)) return false;
            if (File.Exists(Path.Combine(directory, "stop"))) return false;
            var file = new FileInfo(Path.Combine(directory, "heartbeat"));
            if (!file.Exists || file.Length > 256) return false;
            if (!DateTimeOffset.TryParse(File.ReadAllText(file.FullName), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)) return false;
            var age = DateTimeOffset.UtcNow - stamp;
            return age >= -TimeSpan.FromSeconds(1) && age <= TimeSpan.FromSeconds(6);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }
}

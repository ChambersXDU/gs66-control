using System.Diagnostics;
using System.Text.Json;
using Gs66Control.Core;

namespace Gs66Control.Helper;

/// <summary>
/// Cross-process validation for the fixed request, parent identity and
/// heartbeat contract. Program.cs owns the command routing; it only needs to
/// forward curve-cross-suite, curve-cross-parent and curve-cross-worker here.
/// No hardware backend is reachable from these methods.
/// </summary>
internal static class CurveCrossProcessSimulation
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    internal static async Task<int> Run(string output)
    {
        Directory.CreateDirectory(output);
        var scenarios = new[] { "parent-death", "heartbeat-expiration" };
        var results = new List<object>();
        var passed = true;
        var passedCount = 0;
        foreach (var scenario in scenarios)
        {
            var requestId = Guid.NewGuid().ToString("N");
            using var parent = Program.StartChild("curve-cross-parent", requestId, scenario);
            var paths = CurveRequestPaths.Resolve(requestId);
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            var parentTerminated = false;
            while (DateTimeOffset.UtcNow < deadline && !File.Exists(paths.Result))
            {
                if (scenario == "parent-death" && !parentTerminated && IsActive(paths) && !parent.HasExited)
                {
                    parent.Kill(entireProcessTree: false);
                    parentTerminated = true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }

            CurveControlResult? result = null;
            if (File.Exists(paths.Result))
            {
                try { result = JsonSerializer.Deserialize<CurveControlResult>(File.ReadAllText(paths.Result), ReadOptions); }
                catch (JsonException) { }
            }
            if (!parent.HasExited)
            {
                try { parent.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            await parent.WaitForExitAsync().ConfigureAwait(false);
            var ok = result is { Outcome: "Restored", RecoveryVerified: true, IsSimulated: true };
            passed &= ok;
            if (ok) passedCount++;
            results.Add(new { Scenario = scenario, Passed = ok, Result = result });
            Console.WriteLine($"cross-{scenario}: {(ok ? "PASS" : "FAIL")} ({result?.Outcome ?? "no result"})");
        }

        File.WriteAllText(Path.Combine(Path.GetFullPath(output), "curve-cross-process-verification.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{passedCount}/{scenarios.Length} cross-process curve scenarios passed. Hardware writes: 0.");
        return passed ? 0 : 1;
    }

    /// <summary>Entry point for a parent process that owns the heartbeat.</summary>
    internal static async Task<int> Parent(string requestId, string scenario)
    {
        if (!Guid.TryParseExact(requestId, "N", out _)) return 2;
        var process = Process.GetCurrentProcess();
        var request = new CurveControlRequest(
            TargetProfile("cross-" + scenario), process.Id, process.StartTime.ToUniversalTime(), requestId, false);
        var paths = CurveRequestPaths.Resolve(requestId);
        CurveControlStore.WriteRequest(paths, request);
        CurveControlStore.WriteHeartbeat(paths, DateTimeOffset.UtcNow);
        var worker = Program.StartChild("curve-cross-worker", requestId);
        var stoppedHeartbeats = false;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(18);
        while (DateTimeOffset.UtcNow < deadline && !File.Exists(paths.Result))
        {
            if (!stoppedHeartbeats && scenario == "heartbeat-expiration" && IsActive(paths))
                stoppedHeartbeats = true;
            if (!stoppedHeartbeats)
                CurveControlStore.WriteHeartbeat(paths, DateTimeOffset.UtcNow);
            await Task.Delay(250).ConfigureAwait(false);
        }
        await worker.WaitForExitAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>Entry point for the independent worker process.</summary>
    internal static async Task<int> Worker(string requestId)
    {
        var paths = CurveRequestPaths.Resolve(requestId);
        var request = CurveControlStore.ReadRequest(paths);
        var backend = new CurveSimulationBackend();
        var result = await new CurveTransactionGuardian().RunAsync(
            request, backend, new FileCurveGuardianMonitor(),
            @"Global\Gs66Control.CrossProcessCurveSession").ConfigureAwait(false);
        return result.RecoveryVerified ? 0 : 2;
    }

    private static bool IsActive(CurveRequestPaths paths)
    {
        try
        {
            var status = JsonSerializer.Deserialize<CurveControlStatus>(File.ReadAllText(paths.Status), ReadOptions);
            return status?.Phase == "Active";
        }
        catch (Exception) { return false; }
    }

    private static ConsoleProfile TargetProfile(string name) => new(
        name,
        [new(0, 35), new(55, 48), new(60, 59), new(65, 72), new(70, 80), new(90, 88)],
        [new(0, 0), new(50, 48), new(60, 59), new(70, 72), new(82, 80), new(90, 88)])
    { PerformanceShift = 0xC4 };
}

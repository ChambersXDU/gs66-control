using System.Diagnostics;
using System.Text.Json;
using Gs66Control.Core;

namespace Gs66Control.Helper;

internal static class SimulationSuite
{
    private sealed record Case(string Name, string Outcome, byte Mode, int Writes, bool Verified);
    private static readonly Case[] Cases =
    [
        new("normal", "Restored", 141, 2, true),
        new("coordinator", "Restored", 141, 2, true),
        new("apply-automatic", "AutomaticApplied", 13, 1, true),
        new("apply-already-auto", "AutomaticApplied", 13, 0, true),
        new("apply-already-auto-missing-temperature", "AutomaticApplied", 13, 0, true),
        new("apply-parent-crash", "AutomaticFallback", 13, 1, true),
        new("already-auto", "Restored", 13, 0, true),
        new("crash", "AutomaticFallback", 13, 1, true),
        new("force-kill", "AutomaticFallback", 13, 1, true),
        new("hang", "AutomaticFallback", 13, 1, true),
        new("heartbeat-loss", "AutomaticFallback", 13, 1, true),
        new("deadline", "AutomaticFallback", 13, 1, true),
        new("before-start-crash", "Rejected", 141, 0, false),
        new("before-start-concurrent", "Rejected", 13, 0, false),
        new("second-helper", "Restored", 141, 2, true),
        new("concurrent", "RecoveryFailed", 77, 1, false),
        new("concurrent-baseline", "RecoveryFailed", 141, 1, false),
        new("missing-telemetry", "AutomaticFallback", 13, 1, true),
        new("missing-before-start", "Rejected", 141, 0, false),
        new("permission-denied", "RecoveryFailed", 141, 0, false),
        new("lost-write-ack", "AutomaticFallback", 13, 1, true),
        new("recovery-failure", "RecoveryFailed", 13, 1, false),
        new("protocol-invalid", "AutomaticFallback", 13, 1, true),
        new("overlong-command", "AutomaticFallback", 13, 1, true),
        new("provenance-mismatch", "Rejected", 141, 0, false)
    ];

    internal static async Task<int> Run(string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>();
        foreach (var test in Cases)
        {
            var directory = Path.Combine(Path.GetFullPath(output), test.Name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var backend = new SimulationBackend(directory);
            backend.Save(new SimulationState(Mode: test.Name.Contains("already-auto") ? AutoFanPolicy.Automatic : AutoFanPolicy.TrialBaseline,
                GpuTemperature: test.Name is "missing-before-start" or "apply-already-auto-missing-temperature" ? null : 42,
                PermissionDenied: test.Name == "permission-denied", FailAfterWrite: test.Name == "lost-write-ack",
                RecoveryFails: test.Name == "recovery-failure", IsSimulated: test.Name != "provenance-mismatch"));
            using var parent = Program.StartChild("simulation-parent", directory, test.Name);
            var errors = parent.StandardError.ReadToEndAsync();
            var parentOutput = parent.StandardOutput.ReadToEndAsync();
            RecoveryResult? result = null;
            try
            {
                var timer = Stopwatch.StartNew();
                var resultPath = Path.Combine(directory, "result.json");
                while (timer.Elapsed < TimeSpan.FromSeconds(12))
                {
                    if (test.Name == "force-kill" && File.Exists(Path.Combine(directory, "applied.flag")) && !parent.HasExited)
                        parent.Kill();
                    if (File.Exists(resultPath))
                    {
                        try { result = JsonSerializer.Deserialize<RecoveryResult>(File.ReadAllText(resultPath)); }
                        catch (JsonException) { }
                        catch (IOException) { }
                        if (result is not null) break;
                    }
                    await Task.Delay(25);
                }
            }
            finally
            {
                if (!parent.HasExited) parent.Kill(); // The simulated hanging parent is deliberately terminated after independent recovery.
                await parent.WaitForExitAsync();
                var pidPath = Path.Combine(directory, "worker.pid");
                if (File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath), out var pid))
                {
                    try
                    {
                        using var worker = Process.GetProcessById(pid);
                        if (!worker.HasExited) await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                    catch (ArgumentException) { }
                    catch (TimeoutException)
                    {
                        using var worker = Process.GetProcessById(pid);
                        worker.Kill();
                        await worker.WaitForExitAsync();
                    }
                }
            }
            var state = backend.Read();
            var passed = result is not null && result.IsSimulated && result.Outcome == test.Outcome &&
                result.RecoveryVerified == test.Verified && state.Mode == test.Mode && state.Writes == test.Writes;
            results.Add(new { Scenario = test.Name, Passed = passed, IsSimulated = true, result, state.Mode, SimulatedWrites = state.Writes });
            Console.WriteLine($"{test.Name}: {(passed ? "PASS" : "FAIL")} ({result?.Outcome ?? "no result"}, simulated mode {state.Mode}, writes {state.Writes})");
            var errorText = await errors;
            await parentOutput;
            if (!passed)
            {
                Console.Error.WriteLine(errorText);
                File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(results, Program.Json));
                return 1;
            }
        }
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(results, Program.Json));
        Console.WriteLine($"{Cases.Length}/{Cases.Length} independent-process simulation scenarios passed. Hardware writes: 0.");
        return 0;
    }

    internal static async Task<int> Parent(string directory, string scenario)
    {
        if (scenario == "coordinator") return await TrialCoordinator.Run(directory);
        if (scenario is "apply-automatic" or "apply-already-auto" or "apply-already-auto-missing-temperature")
            return await TrialCoordinator.Run(directory, true);
        using var worker = scenario == "apply-parent-crash" ? Program.StartChild("recovery-worker", "simulation", directory, "keep") :
            Program.StartChild("recovery-worker", "simulation", directory);
        File.WriteAllText(Path.Combine(directory, "worker.pid"), worker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var errors = worker.StandardError.ReadToEndAsync();
        if (await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "READY")
        { await worker.WaitForExitAsync(); return 0; }
        if (scenario == "before-start-crash") Environment.Exit(17);
        var backend = new SimulationBackend(directory);
        if (scenario == "before-start-concurrent") backend.Save(backend.Read() with { Mode = AutoFanPolicy.Automatic });
        if (scenario == "second-helper")
        {
            using var contender = Program.StartChild("recovery-worker", "simulation", directory, "contender");
            await contender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var rejection = JsonSerializer.Deserialize<RecoveryResult>(File.ReadAllText(Path.Combine(directory, "contender-result.json")));
            if (rejection?.Outcome != "Rejected" || backend.Read().Writes != 0)
                throw new InvalidOperationException("Concurrent helper was not rejected before writing.");
        }
        await Send(worker, "BEGIN");
        if (await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "APPLIED")
        { await worker.WaitForExitAsync(); return 0; }
        if (scenario is "crash" or "apply-parent-crash") Environment.Exit(17);
        File.WriteAllText(Path.Combine(directory, "applied.flag"), "simulated");
        if (scenario is "hang" or "force-kill") await Task.Delay(Timeout.InfiniteTimeSpan);
        if (scenario == "concurrent") backend.Save(backend.Read() with { Mode = 77 });
        if (scenario == "concurrent-baseline") backend.Save(backend.Read() with { Mode = AutoFanPolicy.TrialBaseline });
        if (scenario == "missing-telemetry") backend.Save(backend.Read() with { GpuTemperature = null });
        if (scenario == "deadline")
        {
            while (!worker.HasExited)
            {
                try { await Send(worker, "HEARTBEAT"); }
                catch (IOException) { break; }
                await Task.Delay(80);
            }
        }
        else if (scenario == "protocol-invalid") await Send(worker, "UNKNOWN");
        else if (scenario == "overlong-command") await Send(worker, new string('X', 128));
        else if (scenario is "heartbeat-loss" or "recovery-failure") { }
        else await Send(worker, "END");
        await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        var error = await errors;
        if (error.Length > 0) Console.Error.WriteLine(error);
        return 0;
    }

    private static async Task Send(Process worker, string command)
    {
        await worker.StandardInput.WriteLineAsync(command);
        await worker.StandardInput.FlushAsync();
    }
}

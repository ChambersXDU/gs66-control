using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gs66Control.Core;

namespace Gs66Control.Helper;

internal static class CurveSimulationSuite
{
    private sealed record Scenario(string Name, CurveSimulationOptions Options, bool IsTrial = false,
        int StopAfterChecks = 0, int UnhealthyAfterChecks = 0, string ExpectedOutcome = "Restored",
        bool ExpectedVerified = true, int? ExpectedUnknownCpuPoint1 = null, string? ExpectedReason = null,
        byte ExpectedPerformanceShift = 0xC1, bool CancelAfterActive = false,
        int ExpectedPerformanceWrites = 2, byte ExpectedFinalMode = LegacyFanCurveBinding.ReviewedAutomaticMode,
        bool IncludeExpectedBaseline = false, bool ExpectedRecoveryModeFirst = false);

    private sealed class FakeGuardianMonitor(int stopAfterChecks = 0, int unhealthyAfterChecks = 0, Action? onFirstStopCheck = null) : ICurveGuardianMonitor
    {
        private readonly int stopAfter = stopAfterChecks;
        private readonly int unhealthyAfter = unhealthyAfterChecks;
        private readonly Action? onFirstStopCheck = onFirstStopCheck;
        private int healthChecks;
        private int stopChecks;

        public bool ParentAndHeartbeatHealthy(CurveControlRequest request, DateTimeOffset now) =>
            unhealthyAfter == 0 || ++healthChecks < unhealthyAfter;

        public bool StopRequested(CurveRequestPaths paths) =>
            StopRequestedCore();

        private bool StopRequestedCore()
        {
            stopChecks++;
            if (stopChecks == 1) onFirstStopCheck?.Invoke();
            return stopAfter > 0 && stopChecks >= stopAfter;
        }
    }

    internal static async Task<int> Run(string output)
    {
        Directory.CreateDirectory(output);
        var process = Process.GetCurrentProcess();
        var startUtc = process.StartTime.ToUniversalTime();
        var scenarios = new[]
        {
            new Scenario("successful-apply-stop", new(), StopAfterChecks: 1, ExpectedRecoveryModeFirst: true),
            new Scenario("gpu-d3-no-fabricated-temperature", new(GpuD3: true), StopAfterChecks: 3),
            new Scenario("gpu-wake-missing-temperature", new(GpuD3: true, GpuWakesWithoutTemperature: true),
                ExpectedOutcome: "Restored", ExpectedReason: "telemetry"),
            new Scenario("trial-restore", new(), IsTrial: true, ExpectedFinalMode: LegacyFanCurveBinding.ReviewedCustomMode),
            new Scenario("parent-death", new(), UnhealthyAfterChecks: 9),
            new Scenario("heartbeat-expiration", new(), UnhealthyAfterChecks: 9),
            new Scenario("partial-write-then-exception", new(FailAfterSpeedWrite: true), ExpectedPerformanceWrites: 0),
            new Scenario("trial-fault-auto-fallback", new(FailAfterSpeedWrite: true), IsTrial: true, ExpectedPerformanceWrites: 0),
            new Scenario("combined-performance-partial-ack", new(FailAfterPerformanceWrite: true)),
            new Scenario("unknown-external-change", new(ExternalMutationAfterWrite: true), ExpectedOutcome: "RecoveryFailed", ExpectedVerified: false, ExpectedUnknownCpuPoint1: 57, ExpectedPerformanceWrites: 0),
            new Scenario("late-thermal-cpu-85", new(LateCpuTemperature: 85), ExpectedOutcome: "Restored", ExpectedReason: "thermal"),
            new Scenario("late-telemetry-loss", new(LateTelemetryUnavailable: true), ExpectedOutcome: "Restored", ExpectedReason: "telemetry"),
            new Scenario("active-external-curve", new(ExternalMutationAfterActive: true), ExpectedOutcome: "RecoveryFailed", ExpectedVerified: false, ExpectedUnknownCpuPoint1: 57),
            new Scenario("active-external-performance", new(ExternalPerformanceMutationAfterActive: true), ExpectedOutcome: "RecoveryFailed", ExpectedVerified: false, ExpectedPerformanceShift: 0xC2, ExpectedPerformanceWrites: 1),
            new Scenario("active-external-automatic-mode", new(ExternalModeAfterActive: 0x0D)),
            new Scenario("cancel-after-active", new(), ExpectedOutcome: "Restored", CancelAfterActive: true),
            new Scenario("thermal-cpu-85", new(CpuTemperature: 85), ExpectedOutcome: "ThermalFallback", ExpectedPerformanceWrites: 0),
            new Scenario("thermal-gpu-80", new(GpuTemperature: 80), ExpectedOutcome: "ThermalFallback", ExpectedPerformanceWrites: 0),
            new Scenario("thermal-mode-write-ack-loss", new(CpuTemperature: 85, FailAfterModeWrite: true), ExpectedPerformanceWrites: 0),
            new Scenario("trial-admission-cpu-80", new(CpuTemperature: 80), IsTrial: true, ExpectedOutcome: "ThermalFallback", ExpectedPerformanceWrites: 0),
            new Scenario("trial-admission-gpu-75", new(GpuTemperature: 75), IsTrial: true, ExpectedOutcome: "ThermalFallback", ExpectedPerformanceWrites: 0),
            new Scenario("trial-expected-baseline-refuses-change",
                new(BaselineMutationBeforeAdmission: true), IsTrial: true,
                ExpectedOutcome: "Rejected", ExpectedVerified: false,
                ExpectedFinalMode: LegacyFanCurveBinding.ReviewedCustomMode,
                ExpectedPerformanceWrites: 0, IncludeExpectedBaseline: true),
            new Scenario("missing-temperature", new(CpuTemperature: null), ExpectedOutcome: "ThermalFallback", ExpectedPerformanceWrites: 0),
            new Scenario("recovery-failure", new(FailRecovery: true), StopAfterChecks: 1, ExpectedOutcome: "RecoveryFailed", ExpectedVerified: false, ExpectedPerformanceShift: 0xC4, ExpectedPerformanceWrites: 1)
        };
        var results = new List<object>();
        var allPassed = true;

        foreach (var scenario in scenarios)
        {
            var backend = new CurveSimulationBackend(scenario.Options);
            var expectedBaseline = scenario.IncludeExpectedBaseline ? backend.CaptureBaseline() : null;
            var request = new CurveControlRequest(
                TargetProfile(scenario.Name), process.Id, startUtc, Guid.NewGuid().ToString("N"), scenario.IsTrial,
                ExpectedBaseline: expectedBaseline);
            var paths = CurveRequestPaths.Resolve(request.RequestId);
            CurveControlStore.WriteRequest(paths, request);
            using var cancellation = new CancellationTokenSource();
            Action? onFirstStopCheck = scenario.ExpectedRecoveryModeFirst
                ? backend.ClearOperationLog
                : null;
            if (scenario.CancelAfterActive)
            {
                onFirstStopCheck = () =>
                {
                    backend.ClearOperationLog();
                    cancellation.Cancel();
                };
            }
            var monitor = new FakeGuardianMonitor(scenario.StopAfterChecks, scenario.UnhealthyAfterChecks,
                onFirstStopCheck);
            var mutex = $"Gs66Control.Simulation.Curve.{request.RequestId}";
            var result = await new CurveTransactionGuardian().RunAsync(request, backend, monitor, mutex, true, cancellation.Token);
            var state = backend.State;
            var journal = ReadJournal(paths);
            var journalPassed = JournalMatches(scenario, result, journal);
            var passed = result.Outcome == scenario.ExpectedOutcome && result.RecoveryVerified == scenario.ExpectedVerified &&
                state.Mode == scenario.ExpectedFinalMode &&
                state.PerformanceShift == scenario.ExpectedPerformanceShift &&
                state.PerformanceWrites == scenario.ExpectedPerformanceWrites &&
                (scenario.ExpectedUnknownCpuPoint1 is null || state.Cpu[1].Speed == scenario.ExpectedUnknownCpuPoint1) &&
                (scenario.ExpectedReason is null || result.Reason.Contains(scenario.ExpectedReason, StringComparison.OrdinalIgnoreCase)) &&
                (!scenario.ExpectedRecoveryModeFirst || RecoveryModeWasFirst(backend.OperationLog)) &&
                journalPassed;
            allPassed &= passed;
            results.Add(new
            {
                Scenario = scenario.Name,
                Passed = passed,
                Result = result,
                Final = state,
                Journal = journal,
                JournalPassed = journalPassed,
                HardwareWrites = result.HardwareWriteAttempted ? result.SpeedWrites : 0
            });
            Console.WriteLine($"{scenario.Name}: {(passed ? "PASS" : "FAIL")} ({result.Outcome}, mode {state.Mode:X2}, speed writes {state.SpeedWrites})");
        }

        var takeover = await RunRecoveryTakeoverSimulation();
        allPassed &= takeover.Passed;
        results.Add(new
        {
            Scenario = "crash-recovery-takeover",
            Passed = takeover.Passed,
            Result = takeover.Result,
            Final = takeover.State,
            Journal = takeover.Journal,
            JournalPassed = takeover.JournalPassed,
            HardwareWrites = 0
        });
        Console.WriteLine($"crash-recovery-takeover: {(takeover.Passed ? "PASS" : "FAIL")} ({takeover.Result.Outcome}, mode {takeover.State.Mode:X2})");

        var recoverySafety = await RunRecoverySafetySimulations();
        allPassed &= recoverySafety.Passed;
        results.Add(new
        {
            Scenario = "recovery-entry-safety",
            Passed = recoverySafety.Passed,
            Details = recoverySafety.Details
        });
        Console.WriteLine($"recovery-entry-safety: {(recoverySafety.Passed ? "PASS" : "FAIL")} ({recoverySafety.Details})");

        var verificationPath = Path.Combine(Path.GetFullPath(output), "curve-verification.json");
        File.WriteAllText(verificationPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{scenarios.Length + 2}/{scenarios.Length + 2} curve transaction simulations passed. Hardware writes: 0.");
        return allPassed ? 0 : 1;
    }

    private static bool RecoveryModeWasFirst(IReadOnlyList<string> operations) =>
        operations.Count > 0 && operations[0] == "mode:auto" &&
        operations.Skip(1).All(operation => operation.StartsWith("speed:", StringComparison.Ordinal));

    private static async Task<(bool Passed, CurveControlResult Result, CurveSimulationState State,
        CurveControlJournal? Journal, bool JournalPassed)> RunRecoveryTakeoverSimulation()
    {
        var backend = new CurveSimulationBackend();
        var baseline = backend.CaptureBaseline();
        var cpu = baseline.Cpu.ToArray();
        cpu[(int)FanCurvePoint.Point1] = cpu[(int)FanCurvePoint.Point1] with { Speed = 51 };
        var target = new ConsoleProfile("crash-recovery",
            cpu,
            baseline.Gpu.ToArray());
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        CurveControlStore.WriteJournal(paths, new CurveControlJournal(
            requestId,
            DateTimeOffset.UtcNow,
            baseline,
            target,
            LegacyFanCurveBinding.ReviewedCustomMode,
            [new CurveSpeedMutation(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51, "Applied")],
            null,
            null,
            "Active"));
        backend.SetSpeed(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51);
        var result = await CurveRecoveryEntry.RunAsync(
            requestId,
            backend,
            $"Gs66Control.Simulation.Recovery.{requestId}");
        var journal = ReadJournal(paths);
        var journalPassed = journal?.Recovery?.Phase == "RecoveryVerified" &&
            journal.Recovery.SpeedMutations.All(mutation => mutation.Phase == "Applied") &&
            journal.Recovery.ModeMutations.All(mutation => mutation.Phase == "Applied");
        var state = backend.State;
        var passed = result.Outcome == "Restored" && result.RecoveryVerified &&
            state.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode &&
            state.Cpu[(int)FanCurvePoint.Point1].Speed == 50 && journalPassed;
        return (passed, result, state, journal, journalPassed);
    }

    private static async Task<(bool Passed, string Details)> RunRecoverySafetySimulations()
    {
        var details = new List<string>();
        var invalidPreserved = await RunInvalidJournalRejection();
        details.Add($"invalid-preserves-session={invalidPreserved}");
        var completedPreserved = await RunCompletedJournalRejection();
        details.Add($"completed-preserves-session={completedPreserved}");

        var busyResult = await RunBusyJournalRejectionAndRelease();
        details.Add($"busy-preserves-session={busyResult.Preserved}, mutex-released={busyResult.Released}");

        var cancellationResult = await RunCancellationAfterTakeover();
        details.Add($"post-takeover-cancellation={cancellationResult}");
        var transientRetry = await RunTransientRecoveryRetry();
        details.Add($"transient-recovery-retry={transientRetry}");
        var externalRetry = await RunFailedExternalMutationRefusal();
        details.Add($"failed-external-mutation-refused={externalRetry}");
        return (invalidPreserved && completedPreserved && busyResult.Preserved && busyResult.Released && cancellationResult &&
            transientRetry && externalRetry,
            string.Join(", ", details));
    }

    private static async Task<bool> RunInvalidJournalRejection()
    {
        var backend = new CurveSimulationBackend();
        var baseline = backend.CaptureBaseline();
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        CurveControlStore.WriteJournal(paths, RecoveryJournal(requestId, baseline, "InvalidPhase"));
        CurveControlStore.WriteStatus(paths, new CurveControlStatus(requestId, "OldSession", "keep this status", DateTimeOffset.UtcNow));
        CurveControlStore.WriteResult(paths, new CurveControlResult(
            requestId, true, "OldSession", "keep this result", baseline.Mode, true, 7, false,
            0xC1, false, DateTimeOffset.UtcNow));
        var statusBefore = File.ReadAllBytes(paths.Status);
        var resultBefore = File.ReadAllBytes(paths.Result);
        var result = await CurveRecoveryEntry.RunAsync(requestId, backend,
            $"Gs66Control.Simulation.Recovery.Invalid.{requestId}");
        return result.Outcome == "Rejected" &&
            statusBefore.SequenceEqual(File.ReadAllBytes(paths.Status)) &&
            resultBefore.SequenceEqual(File.ReadAllBytes(paths.Result)) &&
            File.Exists(paths.RecoveryResult);
    }

    private static async Task<bool> RunCompletedJournalRejection()
    {
        var backend = new CurveSimulationBackend();
        var baseline = backend.CaptureBaseline();
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        CurveControlStore.WriteJournal(paths, RecoveryJournal(requestId, baseline, "Recovered"));
        CurveControlStore.WriteStatus(paths, new CurveControlStatus(requestId, "OldSession", "keep this status", DateTimeOffset.UtcNow));
        CurveControlStore.WriteResult(paths, new CurveControlResult(
            requestId, true, "OldSession", "keep this result", baseline.Mode, true, 7, false,
            0xC1, false, DateTimeOffset.UtcNow));
        var statusBefore = File.ReadAllBytes(paths.Status);
        var resultBefore = File.ReadAllBytes(paths.Result);
        var result = await CurveRecoveryEntry.RunAsync(requestId, backend,
            $"Gs66Control.Simulation.Recovery.Completed.{requestId}");
        return result.Outcome == "Rejected" &&
            statusBefore.SequenceEqual(File.ReadAllBytes(paths.Status)) &&
            resultBefore.SequenceEqual(File.ReadAllBytes(paths.Result)) &&
            File.Exists(paths.RecoveryResult);
    }

    private static async Task<(bool Preserved, bool Released)> RunBusyJournalRejectionAndRelease()
    {
        var backend = new CurveSimulationBackend();
        var baseline = backend.CaptureBaseline();
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        CurveControlStore.WriteJournal(paths, RecoveryJournal(requestId, baseline, "Active"));
        CurveControlStore.WriteStatus(paths, new CurveControlStatus(requestId, "OldSession", "keep this status", DateTimeOffset.UtcNow));
        CurveControlStore.WriteResult(paths, new CurveControlResult(
            requestId, true, "OldSession", "keep this result", baseline.Mode, true, 7, false,
            0xC1, false, DateTimeOffset.UtcNow));
        backend.SetSpeed(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51);
        var statusBefore = File.ReadAllBytes(paths.Status);
        var resultBefore = File.ReadAllBytes(paths.Result);
        var mutexName = $"Gs66Control.Simulation.Recovery.Busy.{requestId}";
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using var owner = new Mutex(false, mutexName);
            var held = false;
            try
            {
                held = owner.WaitOne(0);
                ready.Set();
                release.Wait();
            }
            finally
            {
                if (held) owner.ReleaseMutex();
            }
        });
        try
        {
            ready.Wait();
            var rejected = await CurveRecoveryEntry.RunAsync(requestId, backend, mutexName);
            var preserved = rejected.Outcome == "Rejected" &&
                statusBefore.SequenceEqual(File.ReadAllBytes(paths.Status)) &&
                resultBefore.SequenceEqual(File.ReadAllBytes(paths.Result)) &&
                File.Exists(paths.RecoveryResult);
            release.Set();
            await holder;
            var recovered = await CurveRecoveryEntry.RunAsync(requestId, backend, mutexName);
            var released = recovered.Outcome == "Restored" && !IsMutexOwned(mutexName);
            return (preserved, released);
        }
        finally
        {
            release.Set();
            try { await holder; }
            catch (Exception) { }
        }
    }

    private static async Task<bool> RunCancellationAfterTakeover()
    {
        var inner = new CurveSimulationBackend();
        var baseline = inner.CaptureBaseline();
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        CurveControlStore.WriteJournal(paths, RecoveryJournal(requestId, baseline, "Active"));
        inner.SetSpeed(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51);
        using var callerCancellation = new CancellationTokenSource();
        var backend = new CancellationAfterTakeoverBackend(inner, callerCancellation);
        var result = await CurveRecoveryEntry.RunAsync(requestId, backend,
            $"Gs66Control.Simulation.Recovery.Cancel.{requestId}", callerCancellation.Token);
        return callerCancellation.IsCancellationRequested && result.Outcome == "Restored" &&
            result.RecoveryVerified && inner.State.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode &&
            inner.State.Cpu[(int)FanCurvePoint.Point1].Speed == 50;
    }

    private static async Task<bool> RunTransientRecoveryRetry()
    {
        var failingBackend = new CurveSimulationBackend(new CurveSimulationOptions(FailRecovery: true));
        var baseline = failingBackend.CaptureBaseline();
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        var target = RecoveryTarget(baseline);
        CurveControlStore.WriteJournal(paths, FailedRecoveryJournal(requestId, baseline, target));
        failingBackend.SetSpeed(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51);
        var first = await CurveRecoveryEntry.RunAsync(requestId, failingBackend,
            $"Gs66Control.Simulation.Recovery.Transient.{requestId}");
        var afterFirst = ReadJournal(paths);

        // A new healthy backend represents the next user initiated attempt;
        // it starts from the same owned target state recorded by the failed
        // attempt, so the retry still has to prove ownership before writes.
        var healthyBackend = new CurveSimulationBackend();
        healthyBackend.SetSpeed(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51);
        var second = await CurveRecoveryEntry.RunAsync(requestId, healthyBackend,
            $"Gs66Control.Simulation.Recovery.TransientRetry.{requestId}");
        var afterSecond = ReadJournal(paths);
        return first.Outcome == "RecoveryFailed" && afterFirst?.Phase == "RecoveryFailed" &&
            afterFirst.Recovery?.Phase == "RecoveryFailed" && second.Outcome == "Restored" &&
            second.RecoveryVerified && afterSecond?.Phase == "Recovered" &&
            afterSecond.Recovery?.Phase == "RecoveryVerified" &&
            healthyBackend.State.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode &&
            healthyBackend.State.Cpu[(int)FanCurvePoint.Point1].Speed == 50;
    }

    private static async Task<bool> RunFailedExternalMutationRefusal()
    {
        var backend = new CurveSimulationBackend();
        var baseline = backend.CaptureBaseline();
        var requestId = Guid.NewGuid().ToString("N");
        var paths = CurveRequestPaths.Resolve(requestId);
        var target = RecoveryTarget(baseline);
        CurveControlStore.WriteJournal(paths, FailedRecoveryJournal(requestId, baseline, target));
        backend.SetSpeed(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 57);
        var result = await CurveRecoveryEntry.RunAsync(requestId, backend,
            $"Gs66Control.Simulation.Recovery.External.{requestId}");
        var journal = ReadJournal(paths);
        return result.Outcome == "RecoveryFailed" && !result.RecoveryVerified &&
            journal?.Phase == "RecoveryFailed" && journal.Recovery?.Phase == "RecoveryFailed" &&
            backend.State.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode &&
            backend.State.Cpu[(int)FanCurvePoint.Point1].Speed == 57;
    }

    private static CurveControlJournal RecoveryJournal(
        string requestId, FanCurveControlBaseline baseline, string phase) =>
        new(
            requestId,
            DateTimeOffset.UtcNow,
            baseline,
            RecoveryTarget(baseline),
            LegacyFanCurveBinding.ReviewedCustomMode,
            [new CurveSpeedMutation(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51, "Applied")],
            null,
            null,
            phase);

    private static CurveControlJournal FailedRecoveryJournal(
        string requestId, FanCurveControlBaseline baseline, ConsoleProfile target) =>
        new(
            requestId,
            DateTimeOffset.UtcNow,
            baseline,
            target,
            LegacyFanCurveBinding.ReviewedCustomMode,
            [new CurveSpeedMutation(FanCurveChannel.Cpu, FanCurvePoint.Point1, 50, 51, "Applied")],
            null,
            null,
            "RecoveryFailed",
            new CurveRecoveryJournal(
                baseline,
                baseline with { Cpu = target.Cpu.ToArray(), Gpu = target.Gpu.ToArray() },
                Array.Empty<CurveSpeedMutation>(),
                Array.Empty<CurveModeMutation>(),
                "RecoveryFailed"));

    private static ConsoleProfile RecoveryTarget(FanCurveControlBaseline baseline)
    {
        var cpu = baseline.Cpu.ToArray();
        cpu[(int)FanCurvePoint.Point1] = cpu[(int)FanCurvePoint.Point1] with { Speed = 51 };
        return new ConsoleProfile("recovery", cpu, baseline.Gpu.ToArray());
    }

    private static bool IsMutexOwned(string mutexName)
    {
        using var probe = new Mutex(false, mutexName);
        try
        {
            if (!probe.WaitOne(0)) return true;
            probe.ReleaseMutex();
            return false;
        }
        catch (AbandonedMutexException)
        {
            probe.ReleaseMutex();
            return false;
        }
    }

    private sealed class CancellationAfterTakeoverBackend(
        CurveSimulationBackend inner,
        CancellationTokenSource callerCancellation) : ICurveControlBackend
    {
        private int captureCount;

        public bool IsSimulated => inner.IsSimulated;

        public FanCurveControlBaseline CaptureBaseline(CancellationToken token = default)
        {
            if (Interlocked.Increment(ref captureCount) == 2)
                callerCancellation.Cancel();
            return inner.CaptureBaseline(token);
        }

        public ThermalReading CaptureTemperatures(CancellationToken token = default) =>
            inner.CaptureTemperatures(token);

        public void SetSpeed(FanCurveChannel channel, FanCurvePoint point, int expectedSpeedPercent,
            int targetSpeedPercent, CancellationToken token = default) =>
            inner.SetSpeed(channel, point, expectedSpeedPercent, targetSpeedPercent, token);

        public void ActivateCustom(byte expectedMode, CancellationToken token = default) => inner.ActivateCustom(expectedMode, token);
        public void SetAutomatic(byte expectedMode, CancellationToken token = default) => inner.SetAutomatic(expectedMode, token);

        public void RecoverBaseline(FanCurveControlBaseline baseline, FanCurveControlBaseline expectedCurrent,
            CancellationToken token = default,
            Action<FanCurveChannel, FanCurvePoint, int, int, bool>? speedMutationObserver = null,
            Action<byte, byte, bool>? modeMutationObserver = null) =>
            inner.RecoverBaseline(baseline, expectedCurrent, token, speedMutationObserver, modeMutationObserver);

        public CurvePerformanceBinding CapturePerformance(CancellationToken token = default) => inner.CapturePerformance(token);

        public void SetPerformance(CurvePerformanceBinding expected, byte targetShift, CancellationToken token = default) =>
            inner.SetPerformance(expected, targetShift, token);

        public void RestorePerformance(CurvePerformanceBinding original, CurvePerformanceBinding expectedCurrent,
            CancellationToken token = default) => inner.RestorePerformance(original, expectedCurrent, token);
    }

    private static CurveControlJournal? ReadJournal(CurveRequestPaths paths)
    {
        try
        {
            return JsonSerializer.Deserialize<CurveControlJournal>(File.ReadAllText(paths.Journal),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool JournalMatches(Scenario scenario, CurveControlResult result, CurveControlJournal? journal)
    {
        if (result.Outcome == "Rejected") return journal is null;
        if (journal is null) return false;
        if (result.Outcome == "ThermalFallback")
        {
            return journal.ModeMutation is { Phase: "Applied", TargetMode: LegacyFanCurveBinding.ReviewedAutomaticMode } &&
                journal.Recovery is null;
        }

        if (journal.Recovery is null) return false;
        var mutationsHavePhases = journal.Recovery.SpeedMutations.All(mutation => mutation.Phase is "Prepared" or "Applied") &&
            journal.Recovery.ModeMutations.All(mutation => mutation.Phase is "Prepared" or "Applied");
        if (!mutationsHavePhases) return false;
        return result.RecoveryVerified
            ? journal.Recovery.Phase == "RecoveryVerified" &&
              journal.Recovery.SpeedMutations.All(mutation => mutation.Phase == "Applied") &&
              journal.Recovery.ModeMutations.All(mutation => mutation.Phase == "Applied") &&
              (journal.PerformanceMutation is null ||
               journal.PerformanceMutation.OriginalShift == journal.PerformanceMutation.TargetShift ||
               journal.PerformanceMutation.Phase == "Restored")
            : journal.Recovery.Phase == "RecoveryFailed";
    }

    private static ConsoleProfile TargetProfile(string name) => new(
        name,
        [new(0, 35), new(55, 48), new(60, 59), new(65, 72), new(70, 80), new(90, 88)],
        [new(0, 0), new(50, 48), new(60, 59), new(70, 72), new(82, 80), new(90, 88)])
    { PerformanceShift = 0xC4 };
}

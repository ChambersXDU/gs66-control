using Gs66Control.Core;

namespace Gs66Control.Helper;

/// <summary>
/// Resumes recovery from one fixed request journal after the owning helper
/// disappears. It never replays the request target: the journal's captured
/// baseline and ownership proof are the only recovery inputs.
/// </summary>
internal static class CurveRecoveryEntry
{
    private const string DefaultMutexName = @"Global\Gs66Control.HardwareControl";
    private static readonly HashSet<string> RecoverablePhases =
    [
        "Prepared", "Writing", "WritingPerformance", "Applied", "Active", "Restoring",
        "ThermalFallbackPrepared", "ThermalFallbackApplied", "RestoringPerformance",
        "RecoveryPrepared", "Recovering"
    ];

    internal static Task<CurveControlResult> RunAsync(
        string requestId,
        ICurveControlBackend backend,
        string mutexName = DefaultMutexName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Run(requestId, backend, mutexName, cancellationToken));

    private static CurveControlResult Run(
        string requestId,
        ICurveControlBackend backend,
        string mutexName,
        CancellationToken cancellationToken)
    {
        CurveRequestPaths? paths = null;
        Mutex? mutex = null;
        var ownsMutex = false;
        var recoveryStarted = false;
        try
        {
            paths = CurveRequestPaths.Resolve(requestId);
            cancellationToken.ThrowIfCancellationRequested();

            // This first read is only a cheap pre-lock gate. The journal is
            // read and validated again after owning the mutex to close the
            // writer/recovery TOCTOU window.
            var journal = CurveControlStore.ReadJournal(paths);
            ValidateJournal(journal, backend);

            mutex = new Mutex(false, mutexName);
            try { ownsMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex)
                return FinishAttempt(paths, requestId, backend,
                    "Rejected", "Another hardware-control session owns the recovery mutex.", null,
                    false, 0, false);

            cancellationToken.ThrowIfCancellationRequested();
            journal = CurveControlStore.ReadJournal(paths);
            ValidateJournal(journal, backend);

            // Recovery owns its own bounded lifetime. Once the recovery
            // journal is about to be committed, cancellation from the caller
            // must not abandon already-written hardware state.
            using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var recoveryToken = recoveryTimeout.Token;
            var live = backend.CaptureBaseline(recoveryToken);
            var freshBlockers = FanCurveControlPolicy.Blockers(
                live, live.Cpu, live.Gpu, backend.IsSimulated, DateTimeOffset.UtcNow);
            if (freshBlockers.Count > 0)
                throw new InvalidDataException("Fresh live binding is outside the reviewed shape. " + string.Join(" ", freshBlockers));
            if (live.IsSimulated != journal.Baseline.IsSimulated ||
                !string.Equals(live.Source, journal.Baseline.Source, StringComparison.Ordinal) ||
                live.Cpu.Count != journal.Baseline.Cpu.Count ||
                live.Gpu.Count != journal.Baseline.Gpu.Count)
                throw new InvalidDataException("Fresh live binding provenance or shape does not match the journal.");

            recoveryStarted = true;
            CurveControlStore.WriteStatus(paths, new CurveControlStatus(
                requestId, "RecoveryPrepared", "Independent journal recovery is taking ownership.", DateTimeOffset.UtcNow));
            var restored = CurveTransactionGuardian.TryRestore(
                paths,
                backend,
                journal.Baseline,
                live,
                journal,
                "Independent journal recovery completed",
                restoreOriginalMode: false,
                recoveryToken,
                out var final,
                out var finalPerformance,
                out var reason);
            return FinishSession(paths, requestId, backend,
                restored ? "Restored" : "RecoveryFailed", reason,
                final?.Mode, restored, journal.Recovery?.SpeedMutations.Count ?? 0,
                journal.Mutations.Count > 0,
                finalPerformance?.RawShift,
                journal.PerformanceMutation is { Phase: "RestoringPrepared" or "Restored" });
        }
        catch (Exception exception)
        {
            return recoveryStarted
                ? FinishSession(paths, requestId, backend, "RecoveryFailed", exception.Message,
                    null, false, 0, false)
                : FinishAttempt(paths, requestId, backend, "Rejected", exception.Message,
                    null, false, 0, false);
        }
        finally
        {
            // Mutex ownership is thread-affine. Release explicitly on this
            // same thread before disposing the handle; Dispose alone does not
            // release an owned named mutex.
            if (mutex is not null)
            {
                try
                {
                    if (ownsMutex) mutex.ReleaseMutex();
                }
                catch (ApplicationException) { }
                catch (ObjectDisposedException) { }
                finally
                {
                    mutex.Dispose();
                }
            }
        }
    }

    private static void ValidateJournal(CurveControlJournal journal, ICurveControlBackend backend)
    {
        var retryableFailure = journal.Phase == "RecoveryFailed" &&
            journal.Recovery?.Phase == "RecoveryFailed";
        if (!RecoverablePhases.Contains(journal.Phase) && !retryableFailure)
            throw new InvalidDataException("The curve journal is not in a recoverable phase.");
        if (journal.Baseline is null || journal.Target is null ||
            journal.Baseline.IsSimulated != backend.IsSimulated ||
            journal.Baseline.CapturedAtUtc == default ||
            string.IsNullOrWhiteSpace(journal.Baseline.Source))
            throw new InvalidDataException("The curve journal binding provenance is invalid.");

        var shapeBlockers = FanCurveControlPolicy.Blockers(
            journal.Baseline, journal.Baseline.Cpu, journal.Baseline.Gpu,
            backend.IsSimulated, journal.Baseline.CapturedAtUtc.AddSeconds(1));
        if (shapeBlockers.Count > 0)
            throw new InvalidDataException("The curve journal baseline is outside the reviewed binding. " + string.Join(" ", shapeBlockers));

        var targetBlockers = FanCurveControlPolicy.Blockers(
            journal.Baseline, journal.Target.Cpu, journal.Target.Gpu,
            backend.IsSimulated, journal.Baseline.CapturedAtUtc.AddSeconds(1));
        if (targetBlockers.Count > 0)
            throw new InvalidDataException("The curve journal target shape is invalid. " + string.Join(" ", targetBlockers));

        var seen = new HashSet<(FanCurveChannel Channel, FanCurvePoint Point)>();
        foreach (var mutation in journal.Mutations)
        {
            if (!seen.Add((mutation.Channel, mutation.Point)))
                throw new InvalidDataException("The curve journal contains duplicate speed mutations.");
            if (mutation.Phase is not ("Prepared" or "Applied"))
                throw new InvalidDataException("The curve journal contains an invalid speed mutation phase.");
            var baselinePoint = Point(journal.Baseline, mutation.Channel, mutation.Point);
            var targetPoint = Point(journal.Target, mutation.Channel, mutation.Point);
            if (mutation.BeforeSpeedPercent != baselinePoint.Speed ||
                mutation.TargetSpeedPercent != targetPoint.Speed ||
                mutation.BeforeSpeedPercent == mutation.TargetSpeedPercent)
                throw new InvalidDataException("The curve journal speed mutation does not match its baseline and target.");
        }

        if (journal.ModeMutation is not null)
        {
            if (journal.ModeMutation.Phase is not ("Prepared" or "Applied") ||
                journal.ModeMutation.BeforeMode != journal.Baseline.Mode ||
                !LegacyFanCurveBinding.IsReviewedMode(journal.ModeMutation.TargetMode))
                throw new InvalidDataException("The curve journal mode mutation is invalid.");
        }

        if (journal.PerformanceMutation is not null)
        {
            if (!ReviewedShift(journal.PerformanceMutation.OriginalShift) ||
                !ReviewedShift(journal.PerformanceMutation.TargetShift) ||
                journal.PerformanceMutation.Phase is not
                    ("Planned" or "Unchanged" or "Prepared" or "Applied" or "RestoringPrepared" or "Restored"))
                throw new InvalidDataException("The curve journal performance mutation is invalid.");
        }

        if (journal.Recovery is not null)
        {
            if (journal.Recovery.Baseline is null || journal.Recovery.Expected is null ||
                !SameBinding(journal.Recovery.Baseline, journal.Baseline))
                throw new InvalidDataException("The curve recovery journal baseline is invalid.");
            if (journal.Recovery.Expected.IsSimulated != backend.IsSimulated ||
                journal.Recovery.Expected.CapturedAtUtc == default ||
                !string.Equals(journal.Recovery.Expected.Source, journal.Baseline.Source, StringComparison.Ordinal))
                throw new InvalidDataException("The curve recovery journal expected provenance is invalid.");
            var expectedBlockers = FanCurveControlPolicy.Blockers(
                journal.Recovery.Expected, journal.Recovery.Expected.Cpu, journal.Recovery.Expected.Gpu,
                backend.IsSimulated, journal.Recovery.Expected.CapturedAtUtc.AddSeconds(1));
            if (expectedBlockers.Count > 0)
                throw new InvalidDataException("The curve recovery journal expected binding is invalid. " + string.Join(" ", expectedBlockers));
            if (journal.Recovery.Phase is not ("RecoveryPrepared" or "Recovering" or "RecoveryFailed" or "RecoveryVerified"))
                throw new InvalidDataException("The curve recovery journal phase is invalid.");
            var recoverySeen = new HashSet<(FanCurveChannel Channel, FanCurvePoint Point)>();
            foreach (var mutation in journal.Recovery.SpeedMutations)
            {
                if (!recoverySeen.Add((mutation.Channel, mutation.Point)) || mutation.Phase is not ("Prepared" or "Applied"))
                    throw new InvalidDataException("The curve recovery journal speed phase is invalid.");
                var expectedPoint = Point(journal.Recovery.Expected, mutation.Channel, mutation.Point);
                var baselinePoint = Point(journal.Baseline, mutation.Channel, mutation.Point);
                if (mutation.BeforeSpeedPercent != expectedPoint.Speed ||
                    mutation.TargetSpeedPercent != baselinePoint.Speed ||
                    mutation.BeforeSpeedPercent == mutation.TargetSpeedPercent)
                    throw new InvalidDataException("The curve recovery journal speed mutation is invalid.");
            }
            foreach (var mutation in journal.Recovery.ModeMutations)
            {
                if (mutation.Phase is not ("Prepared" or "Applied") ||
                    !LegacyFanCurveBinding.IsReviewedMode(mutation.BeforeMode) ||
                    !LegacyFanCurveBinding.IsReviewedMode(mutation.TargetMode))
                    throw new InvalidDataException("The curve recovery journal mode mutation is invalid.");
            }
        }
    }

    private static CurvePoint Point(FanCurveControlBaseline baseline, FanCurveChannel channel, FanCurvePoint point) =>
        channel == FanCurveChannel.Cpu ? baseline.Cpu[(int)point] : baseline.Gpu[(int)point];

    private static CurvePoint Point(ConsoleProfile profile, FanCurveChannel channel, FanCurvePoint point) =>
        channel == FanCurveChannel.Cpu ? profile.Cpu[(int)point] : profile.Gpu[(int)point];

    private static bool SameBinding(FanCurveControlBaseline actual, FanCurveControlBaseline expected) =>
        actual.IsSimulated == expected.IsSimulated &&
        string.Equals(actual.Source, expected.Source, StringComparison.Ordinal) &&
        actual.Mode == expected.Mode &&
        actual.Cpu.SequenceEqual(expected.Cpu) && actual.Gpu.SequenceEqual(expected.Gpu);

    private static bool ReviewedShift(byte value) => value is 0xC0 or 0xC1 or 0xC2 or 0xC4;

    private static CurveControlResult FinishAttempt(
        CurveRequestPaths? paths, string requestId, ICurveControlBackend backend,
        string outcome, string reason, byte? finalMode, bool verified,
        int speedWrites, bool writeAttempted, byte? finalPerformanceShift = null,
        bool performanceWriteAttempted = false)
    {
        var result = CreateResult(requestId, backend, outcome, reason, finalMode, verified,
            speedWrites, writeAttempted, finalPerformanceShift, performanceWriteAttempted);
        if (paths is not null)
        {
            try { CurveControlStore.WriteRecoveryResult(paths, result); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static CurveControlResult FinishSession(
        CurveRequestPaths? paths, string requestId, ICurveControlBackend backend,
        string outcome, string reason, byte? finalMode, bool verified,
        int speedWrites, bool writeAttempted, byte? finalPerformanceShift = null,
        bool performanceWriteAttempted = false)
    {
        var result = CreateResult(requestId, backend, outcome, reason, finalMode, verified,
            speedWrites, writeAttempted, finalPerformanceShift, performanceWriteAttempted);
        if (paths is not null)
        {
            try
            {
                CurveControlStore.WriteStatus(paths, new CurveControlStatus(requestId, outcome, reason, result.CompletedAtUtc));
                CurveControlStore.WriteResult(paths, result);
                CurveControlStore.WriteRecoveryResult(paths, result);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static CurveControlResult CreateResult(
        string requestId, ICurveControlBackend backend, string outcome, string reason,
        byte? finalMode, bool verified, int speedWrites, bool writeAttempted,
        byte? finalPerformanceShift, bool performanceWriteAttempted) =>
        new(requestId, backend.IsSimulated, outcome, reason, finalMode, verified,
            speedWrites, writeAttempted && !backend.IsSimulated, finalPerformanceShift,
            performanceWriteAttempted && !backend.IsSimulated, DateTimeOffset.UtcNow);
}

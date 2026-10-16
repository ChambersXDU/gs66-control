using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Helper;

// The request and journal formats are deliberately narrow.  A helper process
// receives a request id, not a path, so a caller cannot redirect a hardware
// operation to an arbitrary file or directory.
internal sealed record CurveControlRequest(
    ConsoleProfile Target,
    int ParentPid,
    DateTimeOffset ParentStartUtc,
    string RequestId,
    bool IsTrial,
    int TrialSeconds = 5,
    FanCurveControlBaseline? ExpectedBaseline = null)
{
    internal void Validate()
    {
        if (!Guid.TryParseExact(RequestId, "N", out _))
            throw new InvalidDataException("Curve request id must be a compact GUID.");
        if (ParentPid <= 0) throw new InvalidDataException("Curve request parent PID is invalid.");
        if (ParentStartUtc == default) throw new InvalidDataException("Curve request parent start time is missing.");
        if (TrialSeconds != 5) throw new InvalidDataException("Only the reviewed five-second trial duration is supported.");
        if (Target is null) throw new InvalidDataException("Curve request target is missing.");

        var errors = new ConsoleSettings(3, true, "#63D7C5", [Target]).Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
    }
}

internal sealed record CurveSpeedMutation(
    FanCurveChannel Channel,
    FanCurvePoint Point,
    int BeforeSpeedPercent,
    int TargetSpeedPercent,
    string Phase);

internal sealed record CurveModeMutation(byte BeforeMode, byte TargetMode, string Phase);

internal sealed record CurvePerformanceMutation(byte OriginalShift, byte TargetShift, string Phase);

internal sealed record CurvePerformanceBinding(
    bool IsSimulated,
    DateTimeOffset CapturedAtUtc,
    byte RawShift,
    WindowsPerformanceSnapshot? NativeSnapshot);

internal sealed record CurveRecoveryJournal(
    FanCurveControlBaseline Baseline,
    FanCurveControlBaseline Expected,
    IReadOnlyList<CurveSpeedMutation> SpeedMutations,
    IReadOnlyList<CurveModeMutation> ModeMutations,
    string Phase);

internal sealed record CurveControlJournal(
    string RequestId,
    DateTimeOffset CreatedAtUtc,
    FanCurveControlBaseline Baseline,
    ConsoleProfile Target,
    byte TargetMode,
    IReadOnlyList<CurveSpeedMutation> Mutations,
    CurveModeMutation? ModeMutation,
    CurvePerformanceMutation? PerformanceMutation,
    string Phase,
    CurveRecoveryJournal? Recovery = null);

internal sealed record CurveControlStatus(
    string RequestId,
    string Phase,
    string Message,
    DateTimeOffset UpdatedAtUtc);

internal sealed record CurveControlResult(
    string RequestId,
    bool IsSimulated,
    string Outcome,
    string Reason,
    byte? FinalMode,
    bool RecoveryVerified,
    int SpeedWrites,
    bool HardwareWriteAttempted,
    byte? FinalPerformanceShift,
    bool PerformanceWriteAttempted,
    DateTimeOffset CompletedAtUtc);

internal sealed class CurveRequestPaths
{
    private CurveRequestPaths(string directory, string requestId)
    {
        Directory = directory;
        RequestId = requestId;
        Request = Path.Combine(directory, "request.json");
        Heartbeat = Path.Combine(directory, "heartbeat");
        Stop = Path.Combine(directory, "stop");
        Journal = Path.Combine(directory, "journal.json");
        Status = Path.Combine(directory, "status.json");
        Result = Path.Combine(directory, "result.json");
        RecoveryResult = Path.Combine(directory, "recovery-result.json");
    }

    internal string Directory { get; }
    internal string RequestId { get; }
    internal string Request { get; }
    internal string Heartbeat { get; }
    internal string Stop { get; }
    internal string Journal { get; }
    internal string Status { get; }
    internal string Result { get; }
    internal string RecoveryResult { get; }

    internal static CurveRequestPaths Resolve(string requestId)
    {
        if (!Guid.TryParseExact(requestId, "N", out _))
            throw new ArgumentException("The curve request id must be a compact GUID.", nameof(requestId));
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Local application data is unavailable.");
        return new(Path.Combine(root, "GS66Control", "curve-requests", requestId), requestId);
    }
}

internal static class CurveControlStore
{
    private const int MaximumRequestBytes = 64 * 1024;
    private const int MaximumJournalBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    internal static void WriteRequest(CurveRequestPaths paths, CurveControlRequest request)
    {
        request.Validate();
        if (!string.Equals(request.RequestId, paths.RequestId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Request id does not match the fixed request directory.");
        WriteJson(paths.Request, request);
        if (new FileInfo(paths.Request).Length > MaximumRequestBytes)
            throw new InvalidDataException("Curve request is larger than 64 KiB.");
    }

    internal static CurveControlRequest ReadRequest(CurveRequestPaths paths)
    {
        var info = new FileInfo(paths.Request);
        if (!info.Exists || info.Length > MaximumRequestBytes)
            throw new InvalidDataException("Curve request is missing or larger than 64 KiB.");
        var request = JsonSerializer.Deserialize<CurveControlRequest>(File.ReadAllText(paths.Request), JsonOptions)
            ?? throw new InvalidDataException("Curve request is empty.");
        request.Validate();
        if (!string.Equals(request.RequestId, paths.RequestId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Request id does not match the fixed request directory.");
        return request;
    }

    internal static void WriteHeartbeat(CurveRequestPaths paths, DateTimeOffset now) =>
        WriteText(paths.Heartbeat, now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    internal static void RequestStop(CurveRequestPaths paths) => WriteText(paths.Stop, "stop");

    internal static void WriteJournal(CurveRequestPaths paths, CurveControlJournal journal) => WriteJson(paths.Journal, journal);
    internal static void WriteStatus(CurveRequestPaths paths, CurveControlStatus status) => WriteJson(paths.Status, status);
    internal static void WriteResult(CurveRequestPaths paths, CurveControlResult result) => WriteJson(paths.Result, result);
    internal static void WriteRecoveryResult(CurveRequestPaths paths, CurveControlResult result) => WriteJson(paths.RecoveryResult, result);

    internal static CurveControlJournal ReadJournal(CurveRequestPaths paths)
    {
        var info = new FileInfo(paths.Journal);
        if (!info.Exists || info.Length > MaximumJournalBytes)
            throw new InvalidDataException("Curve journal is missing or larger than 512 KiB.");
        var journal = JsonSerializer.Deserialize<CurveControlJournal>(File.ReadAllText(paths.Journal), JsonOptions)
            ?? throw new InvalidDataException("Curve journal is empty.");
        if (!string.Equals(journal.RequestId, paths.RequestId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Curve journal id does not match the fixed request directory.");
        return journal;
    }

    private static void WriteJson<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Fixed request path has no directory.");
        System.IO.Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal interface ICurveControlBackend
{
    bool IsSimulated { get; }
    FanCurveControlBaseline CaptureBaseline(CancellationToken token = default);
    ThermalReading CaptureTemperatures(CancellationToken token = default);
    FanCurveControlBaseline CaptureRuntimeBaseline(CancellationToken token = default) => CaptureBaseline(token);
    void SetSpeed(FanCurveChannel channel, FanCurvePoint point, int expectedSpeedPercent, int targetSpeedPercent, CancellationToken token = default);
    void ActivateCustom(byte expectedMode, CancellationToken token = default);
    void SetAutomatic(byte expectedMode, CancellationToken token = default);
    void RecoverBaseline(
        FanCurveControlBaseline baseline,
        FanCurveControlBaseline expectedCurrent,
        CancellationToken token = default,
        Action<FanCurveChannel, FanCurvePoint, int, int, bool>? speedMutationObserver = null,
        Action<byte, byte, bool>? modeMutationObserver = null);
    CurvePerformanceBinding CapturePerformance(CancellationToken token = default);
    void SetPerformance(CurvePerformanceBinding expected, byte targetShift, CancellationToken token = default);
    void RestorePerformance(CurvePerformanceBinding original, CurvePerformanceBinding expectedCurrent, CancellationToken token = default);
}

internal interface ICurveGuardianMonitor
{
    bool ParentAndHeartbeatHealthy(CurveControlRequest request, DateTimeOffset now);
    bool StopRequested(CurveRequestPaths paths);
}

internal sealed class FileCurveGuardianMonitor : ICurveGuardianMonitor
{
    internal static readonly TimeSpan MaximumHeartbeatAge = TimeSpan.FromSeconds(6);

    public bool ParentAndHeartbeatHealthy(CurveControlRequest request, DateTimeOffset now)
    {
        try { return ParentAndHeartbeatHealthy(request, CurveRequestPaths.Resolve(request.RequestId), now); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    internal bool ParentAndHeartbeatHealthy(CurveControlRequest request, CurveRequestPaths paths, DateTimeOffset now)
    {
        if (!ParentAndProcessHealthy(request)) return false;
        try
        {
            var text = File.ReadAllText(paths.Heartbeat).Trim();
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var heartbeat))
                return false;
            var age = now.ToUniversalTime() - heartbeat.ToUniversalTime();
            // The parent can refresh the file between the worker's timestamp
            // sample and this read. A small future skew is therefore valid;
            // the fixed parent identity check still prevents stale owners.
            return age >= -TimeSpan.FromSeconds(1) && age <= MaximumHeartbeatAge;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public bool StopRequested(CurveRequestPaths paths) => File.Exists(paths.Stop);

    private static bool ParentAndProcessHealthy(CurveControlRequest request)
    {
        try
        {
            using var process = Process.GetProcessById(request.ParentPid);
            if (process.HasExited) return false;
            return (process.StartTime.ToUniversalTime() - request.ParentStartUtc).Duration() <= TimeSpan.FromSeconds(5);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}

/// <summary>
/// Owns one curve session, journals every attempted write and recovers only
/// states that can be proven to be either the captured baseline or this
/// request's target. It is also usable by the independent helper worker.
/// </summary>
internal sealed class CurveTransactionGuardian
{
    private const string TrialDeadlineReason = "The five-second trial session reached its restore deadline.";

    internal Task<CurveControlResult> RunAsync(
        CurveControlRequest request,
        ICurveControlBackend backend,
        ICurveGuardianMonitor monitor,
        string mutexName = @"Global\Gs66Control.HardwareControl",
        bool acceleratedSimulation = false,
        CancellationToken cancellationToken = default)
    {
        // The named mutex is thread-owned on Windows. Keep the whole guarded
        // transaction synchronous and return a completed task so the release
        // always occurs on the acquiring thread.
        return Task.FromResult(RunCore(request, backend, monitor, mutexName, acceleratedSimulation, cancellationToken));
    }

    private CurveControlResult RunCore(
        CurveControlRequest request,
        ICurveControlBackend backend,
        ICurveGuardianMonitor monitor,
        string mutexName,
        bool acceleratedSimulation,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var paths = CurveRequestPaths.Resolve(request.RequestId);
        Directory.CreateDirectory(paths.Directory);
        CurveControlJournal? journal = null;
        FanCurveControlBaseline? baseline = null;
        FanCurveControlBaseline? working = null;
        CurvePerformanceBinding? performanceOriginal = null;
        CurvePerformanceBinding? performanceWorking = null;
        var speedWrites = 0;
        var writeAttempted = false;
        var performanceWriteAttempted = false;
        Mutex? mutex = null;
        var ownsMutex = false;

        try
        {
            CurveControlStore.WriteStatus(paths, Status(request, "Starting", "Checking session ownership."));
            mutex = new Mutex(false, mutexName);
            try { ownsMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException)
            {
                // The abandoned mutex is now owned by this thread; the
                // transaction may proceed after recording that ownership.
                ownsMutex = true;
            }
            if (!ownsMutex)
                return Finish(paths, request, backend, "Rejected", "Another curve session owns the write mutex.", null, false, 0, false);

            if (!IsHealthy(request, monitor, paths))
                return Finish(paths, request, backend, "Rejected", "Parent process or heartbeat is unavailable before writes.", null, false, 0, false);

            baseline = backend.CaptureBaseline(cancellationToken);
            working = baseline;
            if (request.ExpectedBaseline is not null)
            {
                var expected = request.ExpectedBaseline;
                if (string.IsNullOrWhiteSpace(expected.Source))
                    throw new InvalidDataException("Expected baseline provenance is missing.");
                var expectedBlockers = FanCurveControlPolicy.Blockers(
                    expected, expected.Cpu, expected.Gpu, backend.IsSimulated, DateTimeOffset.UtcNow);
                if (expectedBlockers.Count > 0)
                    throw new InvalidDataException("Expected baseline is stale or outside the reviewed binding. " +
                        string.Join(" ", expectedBlockers));
                RequireSameState(baseline, expected,
                    "The live binding no longer matches the explicitly requested expected baseline.");
            }
            if (request.Target.PerformanceShift is byte requestedShift)
            {
                performanceOriginal = backend.CapturePerformance(cancellationToken);
                performanceWorking = performanceOriginal;
                if (performanceOriginal.IsSimulated != backend.IsSimulated || !IsReviewedShift(requestedShift) ||
                    !IsReviewedShift(performanceOriginal.RawShift))
                    throw new InvalidDataException("Performance binding is outside the reviewed Shift values.");
            }
            var plan = FanCurveControlPolicy.Preview(
                baseline, request.Target.Cpu, request.Target.Gpu, backend.IsSimulated, DateTimeOffset.UtcNow);
            if (!plan.Eligible)
                return Finish(paths, request, backend, "Rejected", string.Join(" ", plan.Blockers), baseline.Mode, false, 0, false);

            var plannedPerformance = performanceOriginal is not null
                ? new CurvePerformanceMutation(performanceOriginal.RawShift, request.Target.PerformanceShift!.Value,
                    request.Target.PerformanceShift.Value == performanceOriginal.RawShift ? "Unchanged" : "Planned")
                : null;
            journal = new CurveControlJournal(request.RequestId, DateTimeOffset.UtcNow, baseline,
                request.Target, plan.TargetMode, [], null, plannedPerformance, "Prepared");
            CurveControlStore.WriteJournal(paths, journal);
            CurveControlStore.WriteStatus(paths, Status(request, "Prepared", "Baseline and rollback journal committed."));

            var temperatures = backend.CaptureTemperatures(cancellationToken);
            var thermalReason = ThermalBlocker(temperatures, backend.IsSimulated, request.IsTrial);
            if (thermalReason is not null)
            {
                if (baseline.Mode != LegacyFanCurveBinding.ReviewedAutomaticMode)
                {
                    journal = journal with
                    {
                        ModeMutation = new CurveModeMutation(baseline.Mode,
                            LegacyFanCurveBinding.ReviewedAutomaticMode, "Prepared"),
                        Phase = "ThermalFallbackPrepared"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
                CurveControlStore.WriteStatus(paths, Status(request, "ThermalFallback", thermalReason));
                if (baseline.Mode != LegacyFanCurveBinding.ReviewedAutomaticMode)
                {
                    writeAttempted = true;
                    backend.SetAutomatic(baseline.Mode, cancellationToken);
                    journal = journal with
                    {
                        ModeMutation = journal.ModeMutation! with { Phase = "Applied" },
                        Phase = "ThermalFallbackApplied"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
                var finalThermal = backend.CaptureBaseline(cancellationToken);
                var verified = finalThermal.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode &&
                    SameCurves(finalThermal, baseline);
                CurvePerformanceBinding? finalThermalPerformance = null;
                if (performanceOriginal is not null)
                {
                    try
                    {
                        finalThermalPerformance = backend.CapturePerformance(cancellationToken);
                        verified &= finalThermalPerformance.IsSimulated == performanceOriginal.IsSimulated &&
                            finalThermalPerformance.RawShift == performanceOriginal.RawShift;
                    }
                    catch (Exception exception)
                    {
                        verified = false;
                        thermalReason += " Performance Shift read-back was unavailable: " + exception.Message;
                    }
                }
                journal = journal with { Phase = verified ? "ThermalFallbackVerified" : "ThermalFallbackFailed" };
                CurveControlStore.WriteJournal(paths, journal);
                return Finish(paths, request, backend, verified ? "ThermalFallback" : "RecoveryFailed",
                    verified ? thermalReason : "Automatic fallback or performance read-back failed.", finalThermal.Mode,
                    verified, 0, writeAttempted, finalThermalPerformance?.RawShift, performanceWriteAttempted);
            }

            foreach (var change in BuildChanges(baseline, request.Target))
            {
                EnsureHealthy(request, monitor, paths, "Parent process or heartbeat ended before a curve write.");
                var live = backend.CaptureBaseline(cancellationToken);
                RequireSameState(live, working!, "Device state changed before a curve write.");
                var mutation = new CurveSpeedMutation(change.Channel, change.Point, change.Before, change.Target, "Prepared");
                journal = journal with { Mutations = [.. journal.Mutations, mutation], Phase = "Writing" };
                CurveControlStore.WriteJournal(paths, journal);
                writeAttempted = true;
                backend.SetSpeed(change.Channel, change.Point, change.Before, change.Target, cancellationToken);
                speedWrites++;
                working = ReplaceSpeed(working!, change.Channel, change.Point, change.Target);
                journal = journal with
                {
                    Mutations = journal.Mutations.Take(journal.Mutations.Count - 1).Append(mutation with { Phase = "Applied" }).ToArray()
                };
                CurveControlStore.WriteJournal(paths, journal);
            }

            if (performanceOriginal is not null && request.Target.PerformanceShift is byte targetShift &&
                performanceOriginal.RawShift != targetShift)
            {
                EnsureHealthy(request, monitor, paths, "Parent process or heartbeat ended before performance activation.");
                var livePerformance = backend.CapturePerformance(cancellationToken);
                RequireSamePerformance(livePerformance, performanceWorking!, "Performance state changed before activation.");
                journal = journal with
                {
                    PerformanceMutation = journal.PerformanceMutation! with { Phase = "Prepared" },
                    Phase = "WritingPerformance"
                };
                CurveControlStore.WriteJournal(paths, journal);
                performanceWriteAttempted = true;
                writeAttempted = true;
                backend.SetPerformance(performanceWorking!, targetShift, cancellationToken);
                performanceWorking = performanceWorking! with { RawShift = targetShift };
                journal = journal with { PerformanceMutation = journal.PerformanceMutation! with { Phase = "Applied" }, Phase = "Applied" };
                CurveControlStore.WriteJournal(paths, journal);
            }

            EnsureHealthy(request, monitor, paths, "Parent process or heartbeat ended before custom mode activation.");
            var beforeMode = backend.CaptureBaseline(cancellationToken);
            RequireSameState(beforeMode, working!, "Device state changed before custom mode activation.");
            if (working!.Mode != plan.TargetMode)
            {
                journal = journal with { ModeMutation = new CurveModeMutation(working.Mode, plan.TargetMode, "Prepared") };
                CurveControlStore.WriteJournal(paths, journal);
                writeAttempted = true;
                backend.ActivateCustom(working.Mode, cancellationToken);
                working = working with { Mode = plan.TargetMode };
                journal = journal with { ModeMutation = journal.ModeMutation! with { Phase = "Applied" }, Phase = "Applied" };
                CurveControlStore.WriteJournal(paths, journal);
            }

            CurveControlStore.WriteStatus(paths, Status(request, "Active", "Curve session is active."));
            var stopReason = WaitForStop(request, monitor, paths, backend, working!, performanceWorking,
                acceleratedSimulation, cancellationToken);
            CurveControlStore.WriteStatus(paths, Status(request, "Restoring", stopReason));
            var restored = TryRestore(paths, backend, baseline, working, journal,
                stopReason, request.IsTrial && stopReason == TrialDeadlineReason, cancellationToken,
                out var final, out var finalPerformance, out var reason);
            return Finish(paths, request, backend, restored ? "Restored" : "RecoveryFailed", reason,
                final?.Mode, restored, speedWrites, writeAttempted, finalPerformance?.RawShift, performanceWriteAttempted);
        }
        catch (OperationCanceledException exception)
        {
            var reason = exception.Message.Length == 0 ? "Curve transaction was cancelled." : exception.Message;
            var restored = false;
            FanCurveControlBaseline? final = null;
            CurvePerformanceBinding? finalPerformance = null;
            if (writeAttempted && baseline is not null && journal is not null)
            {
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                restored = TryRestore(paths, backend, baseline, working ?? baseline, journal,
                    reason, false, recoveryTimeout.Token, out final, out finalPerformance, out reason);
            }
            return Finish(paths, request, backend, restored ? "Restored" : (writeAttempted ? "RecoveryFailed" : "Rejected"),
                restored ? "The cancelled transaction was recovered." : reason,
                final?.Mode, restored, speedWrites, writeAttempted, finalPerformance?.RawShift, performanceWriteAttempted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reason = exception.Message;
            var restored = false;
            FanCurveControlBaseline? final = null;
            CurvePerformanceBinding? finalPerformance = null;
            if (writeAttempted && baseline is not null && journal is not null)
            {
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                restored = TryRestore(paths, backend, baseline, working ?? baseline, journal,
                    reason, false, recoveryTimeout.Token, out final, out finalPerformance, out reason);
            }
            return Finish(paths, request, backend, restored ? "Restored" : (writeAttempted ? "RecoveryFailed" : "Rejected"),
                restored ? "The transaction failed and its owned changes were restored." : reason,
                final?.Mode, restored, speedWrites, writeAttempted, finalPerformance?.RawShift, performanceWriteAttempted);
        }
        finally
        {
            if (mutex is not null)
            {
                try { if (ownsMutex) mutex.ReleaseMutex(); }
                catch (AbandonedMutexException) { }
                catch (ObjectDisposedException) { }
                mutex.Dispose();
            }
        }
    }

    private static string WaitForStop(
        CurveControlRequest request,
        ICurveGuardianMonitor monitor,
        CurveRequestPaths paths,
        ICurveControlBackend backend,
        FanCurveControlBaseline working,
        CurvePerformanceBinding? performanceWorking,
        bool accelerated,
        CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var trialLength = TimeSpan.FromSeconds(request.TrialSeconds);
        var monitorInterval = accelerated ? TimeSpan.FromMilliseconds(25) : TimeSpan.FromSeconds(2);
        var nextMonitor = DateTimeOffset.UtcNow;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            if (request.IsTrial && now - started >= (accelerated ? TimeSpan.FromMilliseconds(150) : trialLength))
                return TrialDeadlineReason;
            if (monitor.StopRequested(paths)) return "The console requested curve session stop.";
            if (!IsHealthy(request, monitor, paths)) return "Parent process or heartbeat ended; restoring the owned curve.";
            if (now >= nextMonitor)
            {
                ThermalReading? temperatures = null;
                try { temperatures = backend.CaptureTemperatures(token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    return $"Live temperature telemetry became unavailable ({exception.Message}); restoring the owned curve.";
                }
                var thermalReason = ThermalBlocker(temperatures, backend.IsSimulated);
                if (thermalReason is not null)
                    return $"Active-session thermal guard: {thermalReason}";

                var live = backend.CaptureRuntimeBaseline(token);
                RequireSameState(live, working, "Device curve or mode changed during the active session.");
                if (performanceWorking is not null)
                {
                    var livePerformance = backend.CapturePerformance(token);
                    RequireSamePerformance(livePerformance, performanceWorking, "Performance mode changed during the active session.");
                }
                nextMonitor = now + monitorInterval;
            }
            Task.Delay(accelerated ? 10 : 250, token).GetAwaiter().GetResult();
        }
    }

    internal static bool TryRestore(
        CurveRequestPaths paths,
        ICurveControlBackend backend,
        FanCurveControlBaseline baseline,
        FanCurveControlBaseline working,
        CurveControlJournal journal,
        string triggerReason,
        bool restoreOriginalMode,
        CancellationToken token,
        out FanCurveControlBaseline? final,
        out CurvePerformanceBinding? finalPerformance,
        out string reason)
    {
        final = null;
        finalPerformance = null;
        try
        {
            var live = backend.CaptureBaseline(token);
            var curveOwned = true;
            var curveReason = "";
            FanCurveControlBaseline expected;
            try { expected = ReconcileOwnedState(live, baseline, journal); }
            catch (Exception exception)
            {
                curveOwned = false;
                curveReason = exception.Message;
                expected = live;
            }
            CurvePerformanceBinding? livePerformance = null;
            var performanceOwned = true;
            var performanceReason = "";
            if (journal.PerformanceMutation is not null)
            {
                try
                {
                    livePerformance = backend.CapturePerformance(token);
                    var mutation = journal.PerformanceMutation;
                    var attempted = mutation.Phase is "Prepared" or "Applied" or "RestoringPrepared" or "Restored";
                    if (livePerformance.RawShift != mutation.OriginalShift &&
                        (!attempted || livePerformance.RawShift != mutation.TargetShift))
                    {
                        performanceOwned = false;
                        performanceReason = "Performance Shift changed outside this transaction; refusing to overwrite it.";
                    }
                }
                catch (Exception exception)
                {
                    performanceOwned = false;
                    performanceReason = "Performance Shift could not be reconciled: " + exception.Message;
                }
            }
            FanCurveControlBaseline restoredCurves;
            void PrepareRecoveryJournal(FanCurveControlBaseline recoveryExpected, byte recoveryMode)
            {
                var speedMutations = curveOwned
                    ? BuildRecoveryChanges(recoveryExpected, baseline)
                        .Select(change => new CurveSpeedMutation(change.Channel, change.Point,
                            change.Before, change.Target, "Prepared"))
                        .ToArray()
                    : Array.Empty<CurveSpeedMutation>();
                var modeMutations = recoveryExpected.Mode == recoveryMode
                    ? Array.Empty<CurveModeMutation>()
                    : new[] { new CurveModeMutation(recoveryExpected.Mode, recoveryMode, "Prepared") };
                journal = journal with
                {
                    Recovery = new CurveRecoveryJournal(
                        baseline,
                        recoveryExpected,
                        speedMutations,
                        modeMutations,
                        "RecoveryPrepared"),
                    Phase = "RecoveryPrepared"
                };
                CurveControlStore.WriteJournal(paths, journal);
            }

            void ObserveRecoverySpeed(FanCurveChannel channel, FanCurvePoint point,
                int before, int target, bool applied)
            {
                var recovery = journal.Recovery ?? throw new InvalidOperationException("Recovery journal was not prepared.");
                var mutations = recovery.SpeedMutations.ToArray();
                var index = Array.FindIndex(mutations, mutation =>
                    mutation.Channel == channel && mutation.Point == point &&
                    mutation.BeforeSpeedPercent == before && mutation.TargetSpeedPercent == target);
                var updated = new CurveSpeedMutation(channel, point, before, target, applied ? "Applied" : "Prepared");
                if (index < 0)
                    mutations = [.. mutations, updated];
                else if (applied || mutations[index].Phase != "Applied")
                    mutations[index] = updated;
                journal = journal with
                {
                    Recovery = recovery with
                    {
                        SpeedMutations = mutations,
                        Phase = applied ? "Recovering" : "RecoveryPrepared"
                    },
                    Phase = applied ? "Recovering" : "RecoveryPrepared"
                };
                CurveControlStore.WriteJournal(paths, journal);
            }

            void ObserveRecoveryMode(byte before, byte target, bool applied)
            {
                journal = UpdateRecoveryModeJournal(journal, before, target, applied);
                CurveControlStore.WriteJournal(paths, journal);
            }

            if (curveOwned)
            {
                var recoveryMode = restoreOriginalMode
                    ? baseline.Mode
                    : LegacyFanCurveBinding.ReviewedAutomaticMode;
                PrepareRecoveryJournal(expected, recoveryMode);

                // Leave custom mode before lowering any fan point. The mode
                // mutation is journaled before the write, then a fresh read
                // confirms that only the reviewed mode changed. A trial keeps
                // its original 0x8D mode and therefore skips this branch.
                if (!restoreOriginalMode && expected.Mode != recoveryMode)
                {
                    ObserveRecoveryMode(expected.Mode, recoveryMode, false);
                    backend.SetAutomatic(expected.Mode, token);
                    ObserveRecoveryMode(expected.Mode, recoveryMode, true);
                    var afterMode = backend.CaptureBaseline(token);
                    if (afterMode.Mode != recoveryMode || !SameBindingExceptMode(afterMode, expected))
                        throw new IOException("Automatic-mode recovery changed curve state unexpectedly.");
                    expected = afterMode;
                    journal = journal with
                    {
                        Recovery = journal.Recovery! with { Expected = expected }
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }

                var recoveryBaseline = baseline with { Mode = recoveryMode };
                backend.RecoverBaseline(recoveryBaseline, expected, token,
                    ObserveRecoverySpeed, ObserveRecoveryMode);
                restoredCurves = backend.CaptureBaseline(token);
                if (!SameCurves(restoredCurves, baseline) || restoredCurves.Mode != recoveryMode)
                    throw new IOException("Curve baseline read-back does not match.");
            }
            else if (LegacyFanCurveBinding.IsReviewedMode(live.Mode) && live.Mode != LegacyFanCurveBinding.ReviewedAutomaticMode)
            {
                PrepareRecoveryJournal(live, LegacyFanCurveBinding.ReviewedAutomaticMode);
                ObserveRecoveryMode(live.Mode, LegacyFanCurveBinding.ReviewedAutomaticMode, false);
                backend.SetAutomatic(live.Mode, token);
                ObserveRecoveryMode(live.Mode, LegacyFanCurveBinding.ReviewedAutomaticMode, true);
                restoredCurves = backend.CaptureBaseline(token);
            }
            else
            {
                restoredCurves = live;
            }
            final = restoredCurves;

            if (journal.PerformanceMutation is not null && performanceOwned && livePerformance is not null)
            {
                var mutation = journal.PerformanceMutation;
                if (livePerformance.RawShift != mutation.OriginalShift)
                {
                    journal = journal with
                    {
                        PerformanceMutation = mutation with { Phase = "RestoringPrepared" },
                        Phase = "RestoringPerformance"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                    backend.RestorePerformance(
                        new CurvePerformanceBinding(baseline.IsSimulated, baseline.CapturedAtUtc,
                            mutation.OriginalShift, null), livePerformance, token);
                    journal = journal with
                    {
                        PerformanceMutation = journal.PerformanceMutation! with { Phase = "Restored" },
                        Phase = "Recovering"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
                else
                {
                    journal = journal with
                    {
                        PerformanceMutation = mutation with { Phase = "Restored" }
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
                finalPerformance = backend.CapturePerformance(token);
                if (finalPerformance.RawShift != mutation.OriginalShift)
                    throw new IOException("Performance Shift restoration read-back does not match.");
            }
            else if (journal.PerformanceMutation is not null && !performanceOwned)
            {
                if (journal.Recovery is not null)
                {
                    journal = journal with
                    {
                        Recovery = journal.Recovery with { Phase = "RecoveryFailed" },
                        Phase = "RecoveryFailed"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
                reason = (curveOwned ? "Curve changes restored and factory automatic mode verified. " : "Curve ownership could not be proven. ") + performanceReason;
                return false;
            }

            if (!curveOwned)
            {
                if (journal.Recovery is not null)
                {
                    journal = journal with
                    {
                        Recovery = journal.Recovery with { Phase = "RecoveryFailed" },
                        Phase = "RecoveryFailed"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
                reason = "Curve ownership could not be proven; no unknown point was overwritten. " + curveReason;
                return false;
            }
            if (journal.Recovery is not null)
            {
                journal = journal with
                {
                    Recovery = journal.Recovery with { Phase = "RecoveryVerified" },
                    Phase = "Recovered"
                };
                CurveControlStore.WriteJournal(paths, journal);
            }
            reason = triggerReason + (restoreOriginalMode
                ? " Owned curve and performance changes restored and the trial baseline mode verified."
                : " Owned curve and performance changes restored and factory automatic mode verified.");
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            try
            {
                if (journal.Recovery is not null)
                {
                    journal = journal with
                    {
                        Recovery = journal.Recovery with { Phase = "RecoveryFailed" },
                        Phase = "RecoveryFailed"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
            }
            catch (Exception journalException)
            {
                reason += " Recovery journal update failed: " + journalException.Message;
            }
            // Mode-only automatic fallback does not touch an externally changed
            // curve. It is safe when the current mode itself is one of the two
            // reviewed values, while unknown modes remain untouched.
            try
            {
                var live = backend.CaptureBaseline(token);
                if (LegacyFanCurveBinding.IsReviewedMode(live.Mode) && live.Mode != LegacyFanCurveBinding.ReviewedAutomaticMode)
                {
                    if (journal.Recovery is null)
                    {
                        journal = journal with
                        {
                            Recovery = new CurveRecoveryJournal(
                                baseline,
                                live,
                                Array.Empty<CurveSpeedMutation>(),
                                [new CurveModeMutation(live.Mode, LegacyFanCurveBinding.ReviewedAutomaticMode, "Prepared")],
                                "RecoveryPrepared"),
                            Phase = "RecoveryPrepared"
                        };
                    }
                    else
                    {
                        journal = UpdateRecoveryModeJournal(
                            journal, live.Mode, LegacyFanCurveBinding.ReviewedAutomaticMode, false);
                    }
                    CurveControlStore.WriteJournal(paths, journal);
                    backend.SetAutomatic(live.Mode, token);
                    journal = UpdateRecoveryModeJournal(
                        journal, live.Mode, LegacyFanCurveBinding.ReviewedAutomaticMode, true);
                    CurveControlStore.WriteJournal(paths, journal);
                    final = backend.CaptureBaseline(token);
                }
            }
            catch (Exception fallbackException)
            {
                reason += " Automatic fallback failed: " + fallbackException.Message;
            }
            try
            {
                if (journal.Recovery is not null)
                {
                    journal = journal with
                    {
                        Recovery = journal.Recovery with { Phase = "RecoveryFailed" },
                        Phase = "RecoveryFailed"
                    };
                    CurveControlStore.WriteJournal(paths, journal);
                }
            }
            catch (Exception journalException)
            {
                reason += " Recovery journal finalization failed: " + journalException.Message;
            }
            return false;
        }
    }

    private static CurveControlJournal UpdateRecoveryModeJournal(
        CurveControlJournal journal,
        byte before,
        byte target,
        bool applied)
    {
        var recovery = journal.Recovery ?? throw new InvalidOperationException("Recovery journal was not prepared.");
        var mutations = recovery.ModeMutations.ToArray();
        var index = Array.FindIndex(mutations, mutation =>
            mutation.BeforeMode == before && mutation.TargetMode == target);
        var updated = new CurveModeMutation(before, target, applied ? "Applied" : "Prepared");
        if (index < 0)
            mutations = [.. mutations, updated];
        else if (applied || mutations[index].Phase != "Applied")
            mutations[index] = updated;
        return journal with
        {
            Recovery = recovery with
            {
                ModeMutations = mutations,
                Phase = applied ? "Recovering" : "RecoveryPrepared"
            },
            Phase = applied ? "Recovering" : "RecoveryPrepared"
        };
    }

    private static FanCurveControlBaseline ReconcileOwnedState(
        FanCurveControlBaseline live,
        FanCurveControlBaseline baseline,
        CurveControlJournal journal)
    {
        if (live.IsSimulated != baseline.IsSimulated ||
            !string.Equals(live.Source, baseline.Source, StringComparison.Ordinal) ||
            !LegacyFanCurveBinding.IsReviewedMode(live.Mode))
            throw new InvalidOperationException("Device state is outside the reviewed recovery states.");
        var attempted = journal.Mutations.Select(x => (x.Channel, x.Point)).ToHashSet();
        for (var index = 0; index < LegacyFanCurveBinding.PointCount; index++)
        {
            if (live.Cpu[index].Temperature != baseline.Cpu[index].Temperature ||
                live.Gpu[index].Temperature != baseline.Gpu[index].Temperature)
                throw new InvalidOperationException("Firmware temperature breakpoints changed; refusing recovery.");
            CheckPoint(live.Cpu[index].Speed, baseline.Cpu[index].Speed,
                TargetSpeed(journal, FanCurveChannel.Cpu, (FanCurvePoint)index), attempted.Contains((FanCurveChannel.Cpu, (FanCurvePoint)index)), "CPU", index);
            CheckPoint(live.Gpu[index].Speed, baseline.Gpu[index].Speed,
                TargetSpeed(journal, FanCurveChannel.Gpu, (FanCurvePoint)index), attempted.Contains((FanCurveChannel.Gpu, (FanCurvePoint)index)), "GPU", index);
        }
        var targetMode = journal.ModeMutation?.TargetMode ?? journal.TargetMode;
        var modeAttempted = journal.ModeMutation is { Phase: "Prepared" or "Applied" or "ThermalFallbackPrepared" or "ThermalFallbackApplied" };
        if (live.Mode != baseline.Mode && live.Mode != LegacyFanCurveBinding.ReviewedAutomaticMode &&
            (!modeAttempted || live.Mode != targetMode))
            throw new InvalidOperationException("Fan mode changed to an unknown state; refusing recovery.");
        return live;
    }

    private static int? TargetSpeed(CurveControlJournal journal, FanCurveChannel channel, FanCurvePoint point) =>
        journal.Mutations.LastOrDefault(x => x.Channel == channel && x.Point == point)?.TargetSpeedPercent;

    private static void CheckPoint(int current, int baseline, int? target, bool attempted, string channel, int index)
    {
        if (current == baseline) return;
        if (!attempted || target is null || current != target)
            throw new InvalidOperationException($"{channel} curve point {index} changed outside this transaction; refusing recovery.");
    }

    private static IEnumerable<(FanCurveChannel Channel, FanCurvePoint Point, int Before, int Target)> BuildChanges(
        FanCurveControlBaseline baseline, ConsoleProfile target)
    {
        foreach (var channel in new[] { FanCurveChannel.Cpu, FanCurveChannel.Gpu })
        {
            var before = channel == FanCurveChannel.Cpu ? baseline.Cpu : baseline.Gpu;
            var after = channel == FanCurveChannel.Cpu ? target.Cpu : target.Gpu;
            var changes = Enumerable.Range(0, LegacyFanCurveBinding.PointCount)
                .Select(index => ((FanCurvePoint)index, before[index].Speed, after[index].Speed))
                .Where(x => x.Item2 != x.Item3)
                .OrderByDescending(x => x.Item3 > x.Item2)
                .ThenBy(x => x.Item3 > x.Item2 ? -((int)x.Item1) : (int)x.Item1);
            foreach (var change in changes) yield return (channel, change.Item1, change.Item2, change.Item3);
        }
    }

    private static IEnumerable<(FanCurveChannel Channel, FanCurvePoint Point, int Before, int Target)> BuildRecoveryChanges(
        FanCurveControlBaseline current, FanCurveControlBaseline baseline)
    {
        foreach (var channel in new[] { FanCurveChannel.Cpu, FanCurveChannel.Gpu })
        {
            var before = channel == FanCurveChannel.Cpu ? current.Cpu : current.Gpu;
            var after = channel == FanCurveChannel.Cpu ? baseline.Cpu : baseline.Gpu;
            var changes = Enumerable.Range(0, LegacyFanCurveBinding.PointCount)
                .Select(index => ((FanCurvePoint)index, before[index].Speed, after[index].Speed))
                .Where(x => x.Item2 != x.Item3)
                .OrderByDescending(x => x.Item3 > x.Item2)
                .ThenBy(x => x.Item3 > x.Item2 ? -((int)x.Item1) : (int)x.Item1);
            foreach (var change in changes)
                yield return (channel, change.Item1, change.Item2, change.Item3);
        }
    }

    private static FanCurveControlBaseline ReplaceSpeed(FanCurveControlBaseline state, FanCurveChannel channel, FanCurvePoint point, int speed)
    {
        var cpu = state.Cpu.ToArray();
        var gpu = state.Gpu.ToArray();
        if (channel == FanCurveChannel.Cpu) cpu[(int)point] = cpu[(int)point] with { Speed = speed };
        else gpu[(int)point] = gpu[(int)point] with { Speed = speed };
        return state with { Cpu = cpu, Gpu = gpu };
    }

    private static void RequireSameState(FanCurveControlBaseline actual, FanCurveControlBaseline expected, string message)
    {
        if (actual.IsSimulated != expected.IsSimulated || !string.Equals(actual.Source, expected.Source, StringComparison.Ordinal) ||
            actual.Mode != expected.Mode ||
            !actual.Cpu.SequenceEqual(expected.Cpu) || !actual.Gpu.SequenceEqual(expected.Gpu))
            throw new InvalidOperationException(message);
    }

    private static void RequireSamePerformance(
        CurvePerformanceBinding actual,
        CurvePerformanceBinding expected,
        string message)
    {
        if (actual.IsSimulated != expected.IsSimulated || actual.RawShift != expected.RawShift)
            throw new InvalidOperationException(message);
    }

    private static bool IsReviewedShift(byte value) => value is 0xC0 or 0xC1 or 0xC2 or 0xC4;

    private static bool SameCurves(FanCurveControlBaseline actual, FanCurveControlBaseline expected) =>
        actual.Cpu.SequenceEqual(expected.Cpu) && actual.Gpu.SequenceEqual(expected.Gpu);

    private static bool SameBindingExceptMode(FanCurveControlBaseline actual, FanCurveControlBaseline expected) =>
        actual.IsSimulated == expected.IsSimulated &&
        string.Equals(actual.Source, expected.Source, StringComparison.Ordinal) &&
        SameCurves(actual, expected);

    private static string? ThermalBlocker(ThermalReading reading, bool simulation, bool trialAdmission = false)
    {
        var cpu = reading.CpuTemperature; var gpu = reading.GpuTemperature;
        if (cpu is null || (gpu is null && reading.GpuPower?.IsFreshD3(simulation, DateTimeOffset.UtcNow) != true))
            return "CPU or GPU temperature telemetry is unavailable; using factory automatic mode.";
        var cpuLimit = trialAdmission ? 80 : 85;
        var gpuLimit = trialAdmission ? 75 : 80;
        if (cpu <= 0 || cpu >= cpuLimit || !reading.GpuWithin(gpuLimit - 1, simulation, DateTimeOffset.UtcNow))
        {
            var kind = trialAdmission ? "Trial admission guard" : "Thermal guard";
            return $"{kind} blocked curve activation at CPU {cpu}°C / GPU {gpu}°C (limits {cpuLimit}/{gpuLimit}); using factory automatic mode.";
        }
        return null;
    }

    private static bool IsHealthy(CurveControlRequest request, ICurveGuardianMonitor monitor, CurveRequestPaths paths)
    {
        return monitor.ParentAndHeartbeatHealthy(request, DateTimeOffset.UtcNow);
    }

    private static void EnsureHealthy(CurveControlRequest request, ICurveGuardianMonitor monitor, CurveRequestPaths paths, string message)
    {
        if (!IsHealthy(request, monitor, paths)) throw new InvalidOperationException(message);
    }

    private static CurveControlStatus Status(CurveControlRequest request, string phase, string message) =>
        new(request.RequestId, phase, message, DateTimeOffset.UtcNow);

    private static CurveControlResult Finish(
        CurveRequestPaths paths,
        CurveControlRequest request,
        ICurveControlBackend backend,
        string outcome,
        string reason,
        byte? finalMode,
        bool verified,
        int speedWrites,
        bool writeAttempted,
        byte? finalPerformanceShift = null,
        bool performanceWriteAttempted = false)
    {
        var result = new CurveControlResult(request.RequestId, backend.IsSimulated, outcome, reason, finalMode,
            verified, speedWrites, writeAttempted && !backend.IsSimulated, finalPerformanceShift,
            performanceWriteAttempted && !backend.IsSimulated, DateTimeOffset.UtcNow);
        CurveControlStore.WriteStatus(paths, new CurveControlStatus(request.RequestId, outcome, reason, result.CompletedAtUtc));
        CurveControlStore.WriteResult(paths, result);
        return result;
    }
}

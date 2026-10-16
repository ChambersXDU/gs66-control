using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Helper;

/// <summary>
/// The small, immutable request accepted by the independent performance
/// worker. The raw value is intentionally used here so a request cannot
/// smuggle an unreviewed scenario name or firmware value into the backend.
/// </summary>
public sealed record PerformanceActionRequest(Guid RequestId, byte RawRequestedByte, int TrialSeconds)
{
    public const int MaximumJsonBytes = 64 * 1024;
    public const int TrialDurationSeconds = 5;

    public static bool IsReviewedByte(byte value) => value is 0xC0 or 0xC1 or 0xC2 or 0xC4;

    public void Validate()
    {
        if (RequestId == Guid.Empty) throw new InvalidDataException("RequestId must not be empty.");
        if (!IsReviewedByte(RawRequestedByte))
            throw new InvalidDataException("Only reviewed Shift bytes C0, C1, C2 and C4 are accepted.");
        if (TrialSeconds is not (0 or TrialDurationSeconds))
            throw new InvalidDataException("TrialSeconds must be 0 or 5.");
    }
}

public sealed record PerformanceActionJournal(
    Guid RequestId,
    string State,
    DateTimeOffset UpdatedAtUtc,
    byte? OriginalRawByte,
    byte RequestedRawByte,
    byte? CurrentRawByte,
    bool HardwareWriteAttempted,
    bool ReadBackVerified,
    bool RecoveryVerified,
    string? Error);

public sealed record PerformanceActionResult(
    Guid RequestId,
    bool IsSimulated,
    string Outcome,
    string Reason,
    byte RequestedRawByte,
    int TrialSeconds,
    byte? OriginalRawByte,
    byte? FinalRawByte,
    bool HardwareWriteAttempted,
    bool ReadBackVerified,
    bool RecoveryVerified)
{
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

internal interface IPerformanceActionBackend
{
    bool IsSimulated { get; }
    WindowsPerformanceSnapshot CaptureCurrent(CancellationToken cancellationToken = default);
    WindowsPerformancePreview Preview(byte rawRequestedByte, CancellationToken cancellationToken = default);
    WindowsPerformancePreview PreviewRestore(WindowsPerformanceSnapshot original, byte expectedCurrentRawByte);
    WindowsPerformanceApplyResult Apply(WindowsPerformancePreview preview, CancellationToken cancellationToken = default);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsPerformanceActionBackend : IPerformanceActionBackend
{
    private readonly WindowsPerformanceBackend backend = new();

    public bool IsSimulated => false;

    public WindowsPerformanceSnapshot CaptureCurrent(CancellationToken cancellationToken = default) =>
        backend.CaptureCurrent(cancellationToken);

    public WindowsPerformancePreview Preview(
        byte rawRequestedByte,
        CancellationToken cancellationToken = default)
    {
        if (!PerformanceActionRequest.IsReviewedByte(rawRequestedByte))
            throw new ArgumentOutOfRangeException(nameof(rawRequestedByte));
        return backend.Preview(
            Gs66PerformanceMode.Customize,
            (Gs66ReviewedShiftValue)rawRequestedByte,
            cancellationToken);
    }

    public WindowsPerformancePreview PreviewRestore(
        WindowsPerformanceSnapshot original,
        byte expectedCurrentRawByte) =>
        backend.PreviewRestore(original, expectedCurrentRawByte);

    public WindowsPerformanceApplyResult Apply(
        WindowsPerformancePreview preview,
        CancellationToken cancellationToken = default) =>
        backend.Apply(preview, cancellationToken);
}

/// <summary>
/// Owns one performance transaction. The public entry point constructs only
/// the real Windows backend. The internal simulation entry point is separate
/// and marks every result IsSimulated=true.
/// </summary>
public static class PerformanceActionExecutor
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const string HardwareControlMutexName = @"Global\Gs66Control.HardwareControl";

    public static string RequestRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GS66Control", "performance-requests");

    public static string RequestDirectory(Guid requestId)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("RequestId must not be empty.", nameof(requestId));
        return Path.Combine(RequestRoot, requestId.ToString("N"));
    }

    /// <summary>Atomically creates the fixed request folder and request.json.</summary>
    public static string WriteRequest(PerformanceActionRequest request)
    {
        request.Validate();
        var directory = RequestDirectory(request.RequestId);
        Directory.CreateDirectory(directory);
        WriteAtomic(Path.Combine(directory, "request.json"), request);
        if (new FileInfo(Path.Combine(directory, "request.json")).Length > PerformanceActionRequest.MaximumJsonBytes)
            throw new InvalidDataException("request.json exceeds the 64 KiB limit.");
        return directory;
    }

    /// <summary>
    /// Production entry point. It never accepts an injected or simulated
    /// backend, and therefore cannot report a fake successful hardware action.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Task<PerformanceActionResult> RunAsync(
        string requestDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Performance actions require Windows WMI.");
        EnsureProductionRequestDirectory(requestDirectory);
        // Mutex ownership is thread-affine on Windows. Keep acquisition,
        // the synchronous wait for the async transaction, and release on the
        // same worker thread. Simulation deliberately bypasses this path.
        return Task.Run(() => RunProductionLocked(requestDirectory, cancellationToken));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static PerformanceActionResult RunProductionLocked(
        string requestDirectory,
        CancellationToken cancellationToken)
    {
        using var mutex = new Mutex(false, HardwareControlMutexName);
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException)
        {
            // The abandoned mutex is now owned by this thread. The transaction
            // still performs its fresh binding and expected-value checks.
            ownsMutex = true;
        }

        if (!ownsMutex)
            return RejectBusy(requestDirectory);

        try
        {
            return ExecuteAsync(
                requestDirectory,
                new WindowsPerformanceActionBackend(),
                cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Internal-only simulation hook for process tests. Its result provenance
    /// remains true to the simulated backend and is never reported as real.
    /// </summary>
    internal static Task<PerformanceActionResult> RunSimulationAsync(
        string requestDirectory,
        IPerformanceActionBackend backend,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        if (!backend.IsSimulated) throw new ArgumentException("Simulation requires a simulated backend.", nameof(backend));
        return ExecuteAsync(requestDirectory, backend, cancellationToken, delay);
    }

    private static async Task<PerformanceActionResult> ExecuteAsync(
        string requestDirectory,
        IPerformanceActionBackend backend,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        PerformanceActionRequest request;
        try
        {
            request = LoadRequest(requestDirectory);
            request.Validate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            var invalid = new PerformanceActionResult(
                Guid.Empty, backend.IsSimulated, "Rejected", ex.Message,
                0, 0, null, null, false, false, false);
            TryWriteResult(requestDirectory, invalid);
            return invalid;
        }

        var original = (WindowsPerformanceSnapshot?)null;
        var finalRaw = (byte?)null;
        var writeAttempted = false;
        var readBackVerified = false;
        var recoveryVerified = false;
        var applied = false;
        var journalPath = Path.Combine(requestDirectory, "journal.json");

        try
        {
            WriteJournal(journalPath, new PerformanceActionJournal(
                request.RequestId, "Preparing", DateTimeOffset.UtcNow, null,
                request.RawRequestedByte, null, false, false, false, null));

            cancellationToken.ThrowIfCancellationRequested();
            var preview = backend.Preview(request.RawRequestedByte, cancellationToken);
            original = preview.Snapshot;
            finalRaw = original.RawShiftValue;
            if (!preview.CanApply)
                return Finish(request, backend.IsSimulated, "Rejected", string.Join(" ", preview.Blockers),
                    original.RawShiftValue, finalRaw, false, false, false, journalPath);

            WriteJournal(journalPath, new PerformanceActionJournal(
                request.RequestId, "Prepared", DateTimeOffset.UtcNow, original.RawShiftValue,
                request.RawRequestedByte, original.RawShiftValue, false,
                false, false, null));

            cancellationToken.ThrowIfCancellationRequested();
            applied = true;
            writeAttempted = original.RawShiftValue != request.RawRequestedByte;
            var appliedResult = backend.Apply(preview, cancellationToken);
            writeAttempted |= appliedResult.HardwareWritePerformed;
            readBackVerified = appliedResult.After.RawShiftValue == request.RawRequestedByte;
            finalRaw = appliedResult.After.RawShiftValue;
            if (!readBackVerified)
                throw new IOException("Performance mode read-back did not match the requested byte.");

            WriteJournal(journalPath, new PerformanceActionJournal(
                request.RequestId, "Applied", DateTimeOffset.UtcNow, original.RawShiftValue,
                request.RawRequestedByte, finalRaw, writeAttempted, true, false, null));

            if (request.TrialSeconds == PerformanceActionRequest.TrialDurationSeconds &&
                original.RawShiftValue != request.RawRequestedByte)
            {
                await (delay ?? Task.Delay)(TimeSpan.FromSeconds(request.TrialSeconds), cancellationToken);
                var live = backend.CaptureCurrent(cancellationToken);
                finalRaw = live.RawShiftValue;
                if (live.RawShiftValue != request.RawRequestedByte)
                    throw new InvalidOperationException(
                        $"Current Shift byte changed during trial to 0x{live.RawShiftValue:X2}; restore refused.");

                var restorePreview = backend.PreviewRestore(original, request.RawRequestedByte);
                if (!restorePreview.CanApply)
                    throw new InvalidOperationException(string.Join(" ", restorePreview.Blockers));
                WriteJournal(journalPath, new PerformanceActionJournal(
                    request.RequestId, "RestoringPrepared", DateTimeOffset.UtcNow,
                    original.RawShiftValue, request.RawRequestedByte, live.RawShiftValue,
                    writeAttempted, readBackVerified, false, null));
                var restored = backend.Apply(restorePreview, cancellationToken);
                finalRaw = restored.After.RawShiftValue;
                recoveryVerified = finalRaw == original.RawShiftValue;
                if (!recoveryVerified) throw new IOException("Trial restoration read-back failed.");
                WriteJournal(journalPath, new PerformanceActionJournal(
                    request.RequestId, "Restored", DateTimeOffset.UtcNow, original.RawShiftValue,
                    request.RawRequestedByte, finalRaw, writeAttempted || restored.HardwareWritePerformed,
                    true, true, null));
                writeAttempted |= restored.HardwareWritePerformed;
                return Finish(request, backend.IsSimulated, "Restored",
                    "Five-second performance trial restored the verified original Shift byte.",
                    original.RawShiftValue, finalRaw, writeAttempted, true, true, journalPath);
            }

            var outcome = original.RawShiftValue == request.RawRequestedByte ? "AlreadyApplied" : "Applied";
            return Finish(request, backend.IsSimulated, outcome,
                "Requested performance Shift byte was read back successfully.",
                original.RawShiftValue, finalRaw, writeAttempted, true, false, journalPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            var recoveryMessage = ex.Message;
            if (applied && request.TrialSeconds == PerformanceActionRequest.TrialDurationSeconds && original is not null)
            {
                var attemptedRecovery = TryRestoreAfterFailure(
                    backend, original, request.RawRequestedByte, CancellationToken.None,
                    journalPath, request, writeAttempted, readBackVerified, out var observedRaw);
                finalRaw = observedRaw ?? finalRaw;
                recoveryVerified = attemptedRecovery;
                recoveryMessage += attemptedRecovery
                    ? " Trial recovery restored the captured byte."
                    : " Trial recovery did not overwrite a changed or unreadable current byte.";
            }

            var failed = Finish(request, backend.IsSimulated, recoveryVerified ? "Recovered" : "RecoveryFailed",
                recoveryMessage, original?.RawShiftValue, finalRaw, writeAttempted, readBackVerified,
                recoveryVerified, journalPath);
            return failed;
        }
    }

    private static bool TryRestoreAfterFailure(
        IPerformanceActionBackend backend,
        WindowsPerformanceSnapshot original,
        byte expectedCurrentRawByte,
        CancellationToken cancellationToken,
        string journalPath,
        PerformanceActionRequest request,
        bool writeAttempted,
        bool readBackVerified,
        out byte? observedRaw)
    {
        observedRaw = null;
        try
        {
            var live = backend.CaptureCurrent(cancellationToken);
            observedRaw = live.RawShiftValue;
            if (live.RawShiftValue == original.RawShiftValue) return true;
            if (live.RawShiftValue != expectedCurrentRawByte) return false;
            var plan = backend.PreviewRestore(original, expectedCurrentRawByte);
            if (!plan.CanApply) return false;
            WriteJournal(journalPath, new PerformanceActionJournal(
                request.RequestId, "RestoringPrepared", DateTimeOffset.UtcNow,
                original.RawShiftValue, request.RawRequestedByte, live.RawShiftValue,
                writeAttempted, readBackVerified, false, null));
            var result = backend.Apply(plan, cancellationToken);
            observedRaw = result.After.RawShiftValue;
            return observedRaw == original.RawShiftValue;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static PerformanceActionResult RejectBusy(string requestDirectory)
    {
        var requestId = Guid.Empty;
        byte requested = 0;
        var trialSeconds = 0;
        try
        {
            var request = LoadRequest(requestDirectory);
            requestId = request.RequestId;
            requested = request.RawRequestedByte;
            trialSeconds = request.TrialSeconds;
        }
        catch (Exception) { }

        var result = new PerformanceActionResult(
            requestId, false, "Rejected",
            "Another hardware control action is already running; no write was attempted.",
            requested, trialSeconds, null, null, false, false, false);
        TryWriteResult(requestDirectory, result);
        return result;
    }

    private static void EnsureProductionRequestDirectory(string directory)
    {
        var expectedRoot = Path.GetFullPath(RequestRoot) + Path.DirectorySeparatorChar;
        var actual = Path.GetFullPath(directory);
        if (!actual.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Performance requests must live under the fixed GS66Control request root.");
    }

    private static PerformanceActionRequest LoadRequest(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var requestPath = Path.Combine(fullDirectory, "request.json");
        var info = new FileInfo(requestPath);
        if (!info.Exists) throw new FileNotFoundException("request.json is missing.", requestPath);
        if (info.Length > PerformanceActionRequest.MaximumJsonBytes)
            throw new InvalidDataException("request.json exceeds the 64 KiB limit.");

        var request = JsonSerializer.Deserialize<PerformanceActionRequest>(File.ReadAllText(requestPath), Json)
            ?? throw new InvalidDataException("request.json is empty or invalid.");
        var expectedDirectory = request.RequestId.ToString("N");
        if (!Path.GetFileName(fullDirectory).Equals(expectedDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Request directory does not match RequestId.");
        return request;
    }

    private static PerformanceActionResult Finish(
        PerformanceActionRequest request,
        bool isSimulated,
        string outcome,
        string reason,
        byte? originalRaw,
        byte? finalRaw,
        bool writeAttempted,
        bool readBackVerified,
        bool recoveryVerified,
        string journalPath)
    {
        var result = new PerformanceActionResult(
            request.RequestId, isSimulated, outcome, reason,
            request.RawRequestedByte, request.TrialSeconds,
            originalRaw, finalRaw, writeAttempted,
            readBackVerified, recoveryVerified);
        WriteJournal(journalPath, new PerformanceActionJournal(
            request.RequestId, outcome, DateTimeOffset.UtcNow, originalRaw,
            request.RawRequestedByte, finalRaw, writeAttempted, readBackVerified,
            recoveryVerified, reason));
        TryWriteResult(Path.GetDirectoryName(journalPath)!, result);
        return result;
    }

    private static void WriteJournal(string path, PerformanceActionJournal journal) => WriteAtomic(path, journal);

    private static void TryWriteResult(string directory, PerformanceActionResult result)
    {
        try { WriteAtomic(Path.Combine(directory, "result.json"), result); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Output directory unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Json);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }
}

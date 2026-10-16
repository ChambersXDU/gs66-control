using System.Diagnostics;
using Gs66Control.Core;

namespace Gs66Control.Helper;

internal sealed record RecoveryResult(bool IsSimulated, string Outcome, string Reason, byte? FinalMode, bool RecoveryVerified)
{
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? RequestId { get; init; }
}

internal static class RecoveryWorker
{
    internal static RecoveryResult Run(IAutoFanBackend backend, string mutexName, bool acceleratedSimulation, bool keepAutomatic = false)
    {
        using var mutex = new Mutex(false, mutexName);
        bool locked;
        try { locked = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { locked = true; }
        if (!locked) return new(backend.IsSimulated, "Rejected", "Another control helper owns this trial.", null, false);
        try { return RunLocked(backend, acceleratedSimulation, keepAutomatic); }
        finally { mutex.ReleaseMutex(); }
    }

    private static RecoveryResult RunLocked(IAutoFanBackend backend, bool accelerated, bool keepAutomatic)
    {
        var writeAttempted = false;
        var baseline = AutoFanPolicy.Automatic;
        var deadline = TimeSpan.FromSeconds(accelerated ? 2 : AutoFanPolicy.MaximumTrialSeconds);
        var heartbeat = TimeSpan.FromMilliseconds(accelerated ? 400 : 2000);
        var clock = Stopwatch.StartNew();
        try
        {
            var initial = backend.Capture();
            AutoFanPolicy.Require(initial, backend.IsSimulated, !keepAutomatic || initial.Mode != AutoFanPolicy.Automatic);
            if (!backend.IsSimulated && !keepAutomatic && initial.Mode != AutoFanPolicy.TrialBaseline)
                return new(false, "Rejected", "This single approved hardware trial requires baseline 0x8D.", initial.Mode, false);
            baseline = initial.Mode;
            Emit("READY");
            if (ReadCommand(TimeSpan.FromSeconds(3)) != "BEGIN")
                return new(backend.IsSimulated, "Rejected", "No valid start command; no write attempted.", initial.Mode, false);
            var fresh = backend.Capture();
            AutoFanPolicy.Require(fresh, backend.IsSimulated, !keepAutomatic || fresh.Mode != AutoFanPolicy.Automatic);
            if (fresh.Mode != baseline) throw new InvalidOperationException("Fan mode changed before the trial began.");
            clock.Restart();
            writeAttempted = true;
            backend.SetAutomatic(baseline);
            if (backend.Capture().Mode != AutoFanPolicy.Automatic) throw new IOException("Automatic-mode read-back failed.");
            Emit("APPLIED");
            var lastValidation = clock.Elapsed;
            while (clock.Elapsed < deadline)
            {
                var remaining = deadline - clock.Elapsed;
                var command = ReadCommand(remaining < heartbeat ? remaining : heartbeat);
                if (command == "KEEP_AUTOMATIC" && keepAutomatic)
                {
                    var state = backend.Capture();
                    AutoFanPolicy.Require(state, backend.IsSimulated, false);
                    if (state.Mode != AutoFanPolicy.Automatic) throw new InvalidOperationException("Automatic mode changed before confirmation.");
                    return new(backend.IsSimulated, "AutomaticApplied", "Manufacturer automatic mode is verified and remains active.", state.Mode, true);
                }
                if (command == "END" && !keepAutomatic)
                {
                    var state = backend.Capture();
                    AutoFanPolicy.Require(state, backend.IsSimulated);
                    if (state.Mode != AutoFanPolicy.Automatic) throw new InvalidOperationException("Mode changed during the trial; restore refused.");
                    if (clock.Elapsed >= deadline) return Fallback(backend, "Trial deadline expired before restoration.");
                    if (baseline == AutoFanPolicy.TrialBaseline) backend.RestoreTrialBaseline(AutoFanPolicy.Automatic, baseline);
                    var restored = backend.Capture();
                    AutoFanPolicy.Require(restored, backend.IsSimulated);
                    if (restored.Mode != baseline) throw new IOException("Baseline read-back failed.");
                    return new(backend.IsSimulated, "Restored", "Normal completion restored the pre-trial mode.", restored.Mode, true);
                }
                if (command != "HEARTBEAT") return Fallback(backend, "Parent disconnected, stopped responding, or sent an invalid command.");
                if (clock.Elapsed - lastValidation >= TimeSpan.FromSeconds(1))
                {
                    var current = backend.Capture();
                    AutoFanPolicy.Require(current, backend.IsSimulated);
                    if (current.Mode != AutoFanPolicy.Automatic) throw new InvalidOperationException("Another actor changed the mode during the trial.");
                    lastValidation = clock.Elapsed;
                }
            }
            return Fallback(backend, "Absolute trial deadline expired.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return writeAttempted ? Fallback(backend, ex.Message) :
                new(backend.IsSimulated, "Rejected", ex.Message, null, false);
        }
    }

    private static RecoveryResult Fallback(IAutoFanBackend backend, string reason)
    {
        try
        {
            var state = backend.Capture();
            // Missing temperatures must prevent restoration to the prior manual mode, but
            // need not prevent the reviewed manufacturer-automatic fallback.
            AutoFanPolicy.Require(state, backend.IsSimulated, false);
            if (state.Mode != AutoFanPolicy.Automatic)
                throw new InvalidOperationException("External mode change detected; recovery will not overwrite it.");
            backend.SetAutomatic(AutoFanPolicy.Automatic);
            var verified = backend.Capture();
            AutoFanPolicy.Require(verified, backend.IsSimulated, false);
            if (verified.Mode != AutoFanPolicy.Automatic) throw new IOException("Fallback read-back mismatch.");
            return new(backend.IsSimulated, "AutomaticFallback", reason, verified.Mode, true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new(backend.IsSimulated, "RecoveryFailed", $"{reason} Recovery: {ex.Message}", null, false);
        }
    }

    private static string? ReadCommand(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return null;
        // Console.In may implement ReadAsync synchronously. Keep its blocking read off
        // the watchdog thread so an unresponsive parent cannot prevent the deadline.
        try { return Task.Run(ReadBoundedAsync).WaitAsync(timeout).GetAwaiter().GetResult(); }
        catch (TimeoutException) { return null; }
    }

    private static async Task<string?> ReadBoundedAsync()
    {
        var buffer = new char[1];
        var command = new System.Text.StringBuilder();
        while (await Console.In.ReadAsync(buffer.AsMemory()) != 0)
        {
            if (buffer[0] == '\n') return command.ToString().TrimEnd('\r');
            if (command.Length == 16) return "INVALID";
            command.Append(buffer[0]);
        }
        return null;
    }

    private static void Emit(string message)
    {
        // Broken stdout after parent exit cannot stop the independent recovery path.
        try { Console.WriteLine(message); Console.Out.Flush(); }
        catch (IOException) { }
    }
}

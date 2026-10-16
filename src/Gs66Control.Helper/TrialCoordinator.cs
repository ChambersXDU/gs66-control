using System.Text.Json;

namespace Gs66Control.Helper;

internal static class TrialCoordinator
{
    internal static async Task<int> Run(string? simulationDirectory = null, bool keepAutomatic = false, string? requestId = null)
    {
        using var worker = simulationDirectory is null ? (keepAutomatic ? Program.StartChild("recovery-worker", "automatic", requestId ?? Guid.NewGuid().ToString("N")) :
            Program.StartChild("recovery-worker", "hardware")) :
            Program.StartChild("recovery-worker", "simulation", simulationDirectory, keepAutomatic ? "keep" : "trial");
        if (simulationDirectory is not null)
            File.WriteAllText(Path.Combine(simulationDirectory, "worker.pid"), worker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var errors = worker.StandardError.ReadToEndAsync();
        using var heartbeatLifetime = new CancellationTokenSource();
        Task heartbeats = Task.CompletedTask;
        try
        {
            if (await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)) != "READY")
                throw new IOException("Recovery worker did not arm. No trial start command sent.");
            await worker.StandardInput.WriteLineAsync("BEGIN");
            await worker.StandardInput.FlushAsync();
            heartbeats = SendHeartbeats(worker.StandardInput, heartbeatLifetime.Token);
            if (await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)) != "APPLIED")
                throw new IOException("Automatic mode was not acknowledged. Inspect the independent recovery journal.");
            Console.WriteLine("AUTOMATIC_MODE_READ_BACK_CONFIRMED");
            if (!keepAutomatic) await Task.Delay(TimeSpan.FromSeconds(simulationDirectory is null ? 5 : 1));
            heartbeatLifetime.Cancel();
            await heartbeats;
            await worker.StandardInput.WriteLineAsync(keepAutomatic ? "KEEP_AUTOMATIC" : "END");
            await worker.StandardInput.FlushAsync();
            worker.StandardInput.Close();
            var resultText = await worker.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var result = JsonSerializer.Deserialize<RecoveryResult>(resultText)
                ?? throw new IOException("Recovery worker returned no final result.");
            Console.WriteLine(JsonSerializer.Serialize(result, Program.Json));
            return result.Outcome == (keepAutomatic ? "AutomaticApplied" : "Restored") && result.RecoveryVerified ? 0 : 2;
        }
        finally
        {
            heartbeatLifetime.Cancel();
            try { await heartbeats; }
            catch (IOException) { }
            // Closing the parent pipe requests fallback. Never kill the recovery worker.
            worker.StandardInput.Close();
            if (worker.HasExited)
            {
                var error = await errors;
                if (error.Length > 0) Console.Error.WriteLine(error);
            }
        }
    }

    private static async Task SendHeartbeats(StreamWriter input, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(token))
            {
                await input.WriteLineAsync("HEARTBEAT");
                await input.FlushAsync();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

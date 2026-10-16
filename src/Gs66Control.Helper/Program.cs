using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Helper;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["fan-reader", var fanRequest] && OperatingSystem.IsWindows())
                return await FanTelemetryCollector.Run(fanRequest);
            if (args is ["dry-run"] && OperatingSystem.IsWindows())
            {
                var preview = AutoFanPolicy.Preview(new WindowsAutoFanBackend().Capture(), false, DateTimeOffset.UtcNow)
                    with { FirstHardwareWriteApproved = WindowsAutoFanBackend.FirstHardwareWriteApproved };
                Console.WriteLine(JsonSerializer.Serialize(preview, Json));
                return preview.Eligible ? 0 : 2;
            }
            if (args is ["control-preview"] && OperatingSystem.IsWindows())
            {
                var curves = new WindowsCurveControlBackend().CaptureBaseline();
                var thermal = new WindowsAutoFanBackend().Capture();
                var performance = new WindowsPerformanceBackend().CaptureCurrent();
                var blockers = AutoFanPolicy.Blockers(thermal, false, DateTimeOffset.UtcNow)
                    .Concat(FanCurveControlPolicy.Blockers(curves, curves.Cpu, curves.Gpu, false, DateTimeOffset.UtcNow)).ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new { IsSimulated = false, Curves = curves, Thermal = thermal, Performance = performance, Eligible = blockers.Length == 0, Blockers = blockers, ExplicitHardwareWrites = 0 }, Json));
                return blockers.Length == 0 ? 0 : 2;
            }
            if (args is ["simulate-suite", var output]) return await SimulationSuite.Run(output);
            if (args is ["simulate-curves", var curveOutput]) return await CurveSimulationSuite.Run(curveOutput);
            if (args is ["curve-cross-suite", var crossOutput]) return await CurveCrossProcessSimulation.Run(crossOutput);
            if (args is ["curve-cross-parent", var crossRequest, var crossScenario]) return await CurveCrossProcessSimulation.Parent(crossRequest, crossScenario);
            if (args is ["curve-cross-worker", var crossWorkerRequest]) return await CurveCrossProcessSimulation.Worker(crossWorkerRequest);
            if (args is ["curve-worker", var curveRequest] && Guid.TryParseExact(curveRequest, "N", out _) && OperatingSystem.IsWindows())
            {
                var request = CurveControlStore.ReadRequest(CurveRequestPaths.Resolve(curveRequest));
                var result = await new CurveTransactionGuardian().RunAsync(request, new WindowsCurveControlAdapter(), new FileCurveGuardianMonitor());
                return result.RecoveryVerified ? 0 : 2;
            }
            if (args is ["curve-recover", var recoveryRequest] && Guid.TryParseExact(recoveryRequest, "N", out _) && OperatingSystem.IsWindows())
            {
                var result = await CurveRecoveryEntry.RunAsync(recoveryRequest, new WindowsCurveControlAdapter());
                return result.Outcome == "Restored" && result.RecoveryVerified ? 0 : 2;
            }
            if (args is ["performance-worker", var performanceRequest] && Guid.TryParseExact(performanceRequest, "N", out var performanceId) && OperatingSystem.IsWindows())
            {
                var result = await PerformanceActionExecutor.RunAsync(PerformanceActionExecutor.RequestDirectory(performanceId));
                return result.ReadBackVerified && (!result.HardwareWriteAttempted || result.Outcome is "Applied" or "Restored") ? 0 : 2;
            }
            if (args is ["apply-automatic", "--confirmed"] && OperatingSystem.IsWindows())
                return await TrialCoordinator.Run(keepAutomatic: true);
            if (args is ["apply-automatic", "--confirmed", "--request", var requestId] &&
                Guid.TryParseExact(requestId, "N", out _) && OperatingSystem.IsWindows())
                return await TrialCoordinator.Run(keepAutomatic: true, requestId: requestId);
            if (args is ["trial"] && OperatingSystem.IsWindows())
            {
                if (!WindowsAutoFanBackend.FirstHardwareWriteApproved) throw new InvalidOperationException("First hardware write is not approved; trial is locked.");
                return await TrialCoordinator.Run();
            }
            if (args is ["simulation-parent", var directory, var scenario]) return await SimulationSuite.Parent(directory, scenario);
            if (args.Length is 3 or 4 && args[0] == "recovery-worker" && args[1] == "simulation" &&
                (args.Length == 3 || args[3] is "trial" or "keep"))
            {
                var workspace = args[2];
                var keepAutomatic = args.Length == 4 && args[3] == "keep";
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(workspace))));
                var result = RecoveryWorker.Run(new SimulationBackend(workspace), "Gs66Control.Simulation." + key, true, keepAutomatic);
                PublishSimulationResult(Path.Combine(workspace, "result.json"), result);
                try { Console.WriteLine(JsonSerializer.Serialize(result, Json)); }
                catch (IOException) { }
                return result.RecoveryVerified ? 0 : 2;
            }
            if (args is ["recovery-worker", "automatic", var automaticRequest] &&
                Guid.TryParseExact(automaticRequest, "N", out _) && OperatingSystem.IsWindows())
            {
                var result = RecoveryWorker.Run(WindowsAutoFanBackend.ForVerifiedAutomaticMode(), @"Global\Gs66Control.HardwareControl", false, true)
                    with { RequestId = automaticRequest };
                var actionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control");
                Directory.CreateDirectory(actionDirectory);
                var temporary = Path.Combine(actionDirectory, $"auto-fan-action-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporary, JsonSerializer.Serialize(result, Json));
                File.Move(temporary, Path.Combine(actionDirectory, $"auto-fan-action-{automaticRequest}.json"), true);
                try { Console.WriteLine(JsonSerializer.Serialize(result, Json)); }
                catch (IOException) { }
                return result.Outcome == "AutomaticApplied" && result.RecoveryVerified ? 0 : 2;
            }
            if (args is ["recovery-worker", "simulation", var contenderWorkspace, "contender"])
            {
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(contenderWorkspace))));
                var result = RecoveryWorker.Run(new SimulationBackend(contenderWorkspace), "Gs66Control.Simulation." + key, true);
                PublishSimulationResult(Path.Combine(contenderWorkspace, "contender-result.json"), result);
                return result.RecoveryVerified ? 0 : 2;
            }
            if (args is ["recovery-worker", "hardware"] && OperatingSystem.IsWindows())
            {
                if (!WindowsAutoFanBackend.FirstHardwareWriteApproved) throw new InvalidOperationException("First hardware write is not approved; hardware worker is locked.");
                using (var permit = new FileStream(Path.Combine(AppContext.BaseDirectory, "hardware-trial-used"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var stamp = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
                    permit.Write(stamp);
                    permit.Flush(true);
                }
                var result = RecoveryWorker.Run(new WindowsAutoFanBackend(), @"Global\Gs66Control.HardwareControl", false);
                var journalDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control");
                Directory.CreateDirectory(journalDirectory);
                File.WriteAllText(Path.Combine(journalDirectory, "auto-fan-last-trial.json"), JsonSerializer.Serialize(result, Json));
                try { Console.WriteLine(JsonSerializer.Serialize(result, Json)); }
                catch (IOException) { }
                return result.RecoveryVerified ? 0 : 2;
            }
            Console.Error.WriteLine("Usage: dry-run | simulate-suite <output-directory> | apply-automatic --confirmed (Windows, elevation required). The completed single hardware trial remains locked.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static void PublishSimulationResult(string path, RecoveryResult result)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(result, Json));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Process StartChild(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable."))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not start the independent recovery process.");
    }
}

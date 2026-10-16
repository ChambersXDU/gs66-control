using System.Runtime.Versioning;
using Gs66Control.Core;
using Gs66Control.Windows;

namespace Gs66Control.Helper;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCurveControlAdapter : ICurveControlBackend
{
    private readonly WindowsCurveControlBackend backend = new();
    private WindowsCurveRuntimeSession? runtime;

    public bool IsSimulated => false;
    public FanCurveControlBaseline CaptureBaseline(CancellationToken token = default) => backend.CaptureBaseline(token);
    public FanCurveControlBaseline CaptureRuntimeBaseline(CancellationToken token = default) =>
        (runtime ??= backend.OpenReadSession(token)).CaptureBaseline(token);

    public ThermalReading CaptureTemperatures(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return (runtime ??= backend.OpenReadSession(token)).CaptureTemperatures(token);
    }

    public void SetSpeed(FanCurveChannel channel, FanCurvePoint point, int expectedSpeedPercent, int targetSpeedPercent, CancellationToken token = default) =>
        backend.SetSpeed(channel, point, expectedSpeedPercent, targetSpeedPercent, token);

    public void ActivateCustom(byte expectedMode, CancellationToken token = default) => backend.ActivateCustom(expectedMode, token);
    public void SetAutomatic(byte expectedMode, CancellationToken token = default) => backend.SetAutomatic(expectedMode, token);
    public void RecoverBaseline(
        FanCurveControlBaseline baseline,
        FanCurveControlBaseline expectedCurrent,
        CancellationToken token = default,
        Action<FanCurveChannel, FanCurvePoint, int, int, bool>? speedMutationObserver = null,
        Action<byte, byte, bool>? modeMutationObserver = null) =>
        backend.RecoverBaseline(baseline, expectedCurrent, token, speedMutationObserver, modeMutationObserver);

    public CurvePerformanceBinding CapturePerformance(CancellationToken token = default)
    {
        var snapshot = new WindowsPerformanceBackend().CaptureCurrent(token);
        return new(false, snapshot.CapturedAtUtc, snapshot.RawShiftValue, snapshot);
    }

    public void SetPerformance(CurvePerformanceBinding expected, byte targetShift, CancellationToken token = default)
    {
        if (expected.NativeSnapshot is null || expected.RawShift != expected.NativeSnapshot.RawShiftValue)
            throw new InvalidDataException("The performance binding snapshot is incomplete.");
        var current = new WindowsPerformanceBackend().CaptureCurrent(token);
        if (current.RawShiftValue != expected.RawShift)
            throw new InvalidOperationException("Performance Shift changed before update.");
        var backend = new WindowsPerformanceBackend();
        var preview = backend.Preview(Gs66PerformanceMode.Customize, (Gs66ReviewedShiftValue)targetShift, token);
        if (preview.Snapshot.RawShiftValue != expected.RawShift)
            throw new InvalidOperationException("Performance Shift changed during preview.");
        backend.Apply(preview, token);
    }

    public void RestorePerformance(CurvePerformanceBinding original, CurvePerformanceBinding expectedCurrent, CancellationToken token = default)
    {
        if (!IsReviewedShift(original.RawShift) || !IsReviewedShift(expectedCurrent.RawShift))
            throw new InvalidDataException("The performance restoration value is outside the reviewed set.");
        if (expectedCurrent.RawShift == original.RawShift) return;
        SetPerformance(expectedCurrent, original.RawShift, token);
    }

    private static bool IsReviewedShift(byte value) => value is 0xC0 or 0xC1 or 0xC2 or 0xC4;
}

internal sealed record CurveSimulationOptions(
    int? CpuTemperature = 50,
    int? GpuTemperature = 42,
    bool PermissionDenied = false,
    bool FailAfterSpeedWrite = false,
    bool FailAfterModeWrite = false,
    bool ExternalMutationAfterWrite = false,
    bool FailRecovery = false,
    byte CurrentShift = 0xC1,
    bool FailAfterPerformanceWrite = false,
    int? LateCpuTemperature = null,
    int? LateGpuTemperature = null,
    bool GpuD3 = false,
    bool GpuWakesWithoutTemperature = false,
    bool LateTelemetryUnavailable = false,
    bool ExternalMutationAfterActive = false,
    byte? ExternalModeAfterActive = null,
    bool ExternalPerformanceMutationAfterActive = false,
    bool BaselineMutationBeforeAdmission = false);

internal sealed record CurveSimulationState(
    byte Mode,
    IReadOnlyList<CurvePoint> Cpu,
    IReadOnlyList<CurvePoint> Gpu,
    int SpeedWrites,
    int ModeWrites,
    byte PerformanceShift,
    int PerformanceWrites,
    bool IsSimulated = true);

/// <summary>
/// A deterministic, in-memory implementation of the fixed binding. It is
/// intentionally shaped like the real transaction boundary, so simulations
/// exercise journal, ordering, ownership and recovery decisions without WMI.
/// </summary>
internal sealed class CurveSimulationBackend : ICurveControlBackend
{
    internal static readonly IReadOnlyList<CurvePoint> DefaultCpu =
    [new(0, 35), new(55, 50), new(60, 60), new(65, 72), new(70, 80), new(90, 85)];
    internal static readonly IReadOnlyList<CurvePoint> DefaultGpu =
    [new(0, 0), new(50, 50), new(60, 60), new(70, 72), new(82, 80), new(90, 85)];

    private readonly CurveSimulationOptions options;
    private byte mode = LegacyFanCurveBinding.ReviewedCustomMode;
    private byte performanceShift;
    private CurvePoint[] cpu = DefaultCpu.ToArray();
    private CurvePoint[] gpu = DefaultGpu.ToArray();
    private bool externalMutationInjected;
    private bool activeMutationInjected;
    private int speedWrites;
    private int modeWrites;
    private int temperatureReads;
    private int baselineReads;
    private int performanceReads;
    private int performanceWrites;
    private bool externalPerformanceMutationInjected;
    private readonly List<string> operationLog = [];

    internal CurveSimulationBackend(CurveSimulationOptions? options = null)
    {
        this.options = options ?? new CurveSimulationOptions();
        performanceShift = this.options.CurrentShift;
    }

    public bool IsSimulated => true;
    internal CurveSimulationState State => new(mode, cpu.ToArray(), gpu.ToArray(), speedWrites, modeWrites, performanceShift, performanceWrites);
    internal IReadOnlyList<string> OperationLog => operationLog;
    internal void ClearOperationLog() => operationLog.Clear();

    public FanCurveControlBaseline CaptureBaseline(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        baselineReads++;
        if (baselineReads > 1 && options.BaselineMutationBeforeAdmission && !externalMutationInjected)
        {
            cpu[1] = cpu[1] with { Speed = 57 };
            externalMutationInjected = true;
        }
        return new(true, DateTimeOffset.UtcNow, mode, cpu.ToArray(), gpu.ToArray(), "GS66 curve simulation fixture");
    }

    public ThermalReading CaptureTemperatures(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        temperatureReads++;
        if (temperatureReads > 1 && !activeMutationInjected)
        {
            if (options.ExternalMutationAfterActive)
            {
                cpu[1] = cpu[1] with { Speed = 57 };
                activeMutationInjected = true;
            }
            else if (options.ExternalModeAfterActive is byte externalMode)
            {
                mode = externalMode;
                activeMutationInjected = true;
            }
        }
        if (options.LateTelemetryUnavailable && temperatureReads > 1) return new(null, null);
        if (temperatureReads > 1 && (options.LateCpuTemperature is not null || options.LateGpuTemperature is not null))
            return new(options.LateCpuTemperature ?? options.CpuTemperature, options.LateGpuTemperature ?? options.GpuTemperature);
        return new(options.CpuTemperature, options.GpuD3 ? null : options.GpuTemperature,
            options.GpuD3 ? new(true, DateTimeOffset.UtcNow,
                options.GpuWakesWithoutTemperature && temperatureReads > 1 ? 1 : 4, GpuPowerEvidence.WindowsSource) : null);
    }

    public void SetSpeed(FanCurveChannel channel, FanCurvePoint point, int expectedSpeedPercent, int targetSpeedPercent, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (options.PermissionDenied) throw new UnauthorizedAccessException("Simulation write permission denied.");
        var current = Get(channel, point).Speed;
        if (current != expectedSpeedPercent) throw new InvalidOperationException("Simulation expected speed changed.");
        Set(channel, point, Get(channel, point) with { Speed = targetSpeedPercent });
        operationLog.Add($"speed:{channel}:{point}");
        speedWrites++;
        if (options.ExternalMutationAfterWrite && !externalMutationInjected)
        {
            // This value is neither the baseline nor the target of the
            // transaction, so recovery must refuse to overwrite it.
            Set(FanCurveChannel.Cpu, FanCurvePoint.Point1, cpu[1] with { Speed = 57 });
            externalMutationInjected = true;
        }
        if (options.FailAfterSpeedWrite && speedWrites == 1)
            throw new IOException("Simulation lost the write acknowledgement after mutation.");
    }

    public void ActivateCustom(byte expectedMode, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (options.PermissionDenied) throw new UnauthorizedAccessException("Simulation write permission denied.");
        if (mode != expectedMode) throw new InvalidOperationException("Simulation expected mode changed.");
        var target = LegacyFanCurveBinding.ActivateCustomMode(expectedMode);
        if (mode == target) return;
        mode = target;
        operationLog.Add("mode:custom");
        modeWrites++;
    }

    public void SetAutomatic(byte expectedMode, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (options.PermissionDenied) throw new UnauthorizedAccessException("Simulation write permission denied.");
        if (mode != expectedMode) throw new InvalidOperationException("Simulation expected mode changed.");
        var target = LegacyFanCurveBinding.ReviewedAutomaticMode;
        if (mode == target) return;
        mode = target;
        operationLog.Add("mode:auto");
        modeWrites++;
        if (options.FailAfterModeWrite && modeWrites == 1)
            throw new IOException("Simulation lost the automatic-mode write acknowledgement after mutation.");
    }

    public void RecoverBaseline(
        FanCurveControlBaseline baseline,
        FanCurveControlBaseline expectedCurrent,
        CancellationToken token = default,
        Action<FanCurveChannel, FanCurvePoint, int, int, bool>? speedMutationObserver = null,
        Action<byte, byte, bool>? modeMutationObserver = null)
    {
        token.ThrowIfCancellationRequested();
        if (options.PermissionDenied || options.FailRecovery) throw new IOException("Simulation recovery failure.");
        RequireSame(CaptureBaseline(token), expectedCurrent, "Simulation state changed before recovery.");
        var working = expectedCurrent;
        if (working.Mode != baseline.Mode && baseline.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode)
        {
            var beforeMode = working.Mode;
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, false);
            working = working with { Mode = baseline.Mode };
            mode = baseline.Mode;
            operationLog.Add("mode:auto");
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, true);
            var afterMode = CaptureBaseline(token);
            RequireSame(afterMode, working, "Simulation mode changed before curve recovery.");
            working = afterMode;
        }
        foreach (var channel in new[] { FanCurveChannel.Cpu, FanCurveChannel.Gpu })
        {
            var before = channel == FanCurveChannel.Cpu ? working.Cpu : working.Gpu;
            var target = channel == FanCurveChannel.Cpu ? baseline.Cpu : baseline.Gpu;
            var ordered = Enumerable.Range(0, LegacyFanCurveBinding.PointCount)
                .Select(index => ((FanCurvePoint)index, Before: before[index].Speed, Target: target[index].Speed))
                .Where(change => change.Before != change.Target)
                .OrderByDescending(change => change.Target > change.Before)
                .ThenBy(change => change.Target > change.Before ? -(int)change.Item1 : (int)change.Item1);
            foreach (var change in ordered)
            {
                token.ThrowIfCancellationRequested();
                speedMutationObserver?.Invoke(channel, change.Item1, change.Before, change.Target, false);
                Set(channel, change.Item1, Get(channel, change.Item1) with { Speed = change.Target });
                operationLog.Add($"speed:{channel}:{change.Item1}");
                working = CaptureBaseline(token);
                speedMutationObserver?.Invoke(channel, change.Item1, change.Before, change.Target, true);
            }
        }

        if (working.Mode != baseline.Mode)
        {
            var beforeMode = working.Mode;
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, false);
            working = working with { Mode = baseline.Mode };
            operationLog.Add(baseline.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode ? "mode:auto" : "mode:custom");
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, true);
        }
        cpu = working.Cpu.ToArray();
        gpu = working.Gpu.ToArray();
        mode = working.Mode;
    }

    public CurvePerformanceBinding CapturePerformance(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        performanceReads++;
        if (performanceReads > 2 && options.ExternalPerformanceMutationAfterActive && !externalPerformanceMutationInjected)
        {
            performanceShift = 0xC2;
            externalPerformanceMutationInjected = true;
        }
        return new(true, DateTimeOffset.UtcNow, performanceShift, null);
    }

    public void SetPerformance(CurvePerformanceBinding expected, byte targetShift, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (options.PermissionDenied) throw new UnauthorizedAccessException("Simulation performance write permission denied.");
        if (performanceShift != expected.RawShift) throw new InvalidOperationException("Simulation performance Shift changed.");
        performanceShift = targetShift;
        performanceWrites++;
        if (options.FailAfterPerformanceWrite && performanceWrites == 1)
            throw new IOException("Simulation lost the performance write acknowledgement after mutation.");
    }

    public void RestorePerformance(CurvePerformanceBinding original, CurvePerformanceBinding expectedCurrent, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (options.PermissionDenied || options.FailRecovery) throw new IOException("Simulation performance recovery failure.");
        if (performanceShift != expectedCurrent.RawShift) throw new InvalidOperationException("Simulation performance state changed before recovery.");
        performanceShift = original.RawShift;
        performanceWrites++;
    }

    private CurvePoint Get(FanCurveChannel channel, FanCurvePoint point) =>
        channel == FanCurveChannel.Cpu ? cpu[(int)point] : gpu[(int)point];

    private void Set(FanCurveChannel channel, FanCurvePoint point, CurvePoint value)
    {
        if (channel == FanCurveChannel.Cpu) cpu[(int)point] = value;
        else gpu[(int)point] = value;
    }

    private static void RequireSame(FanCurveControlBaseline actual, FanCurveControlBaseline expected, string message)
    {
        if (actual.IsSimulated != expected.IsSimulated || !string.Equals(actual.Source, expected.Source, StringComparison.Ordinal) ||
            actual.Mode != expected.Mode || !actual.Cpu.SequenceEqual(expected.Cpu) || !actual.Gpu.SequenceEqual(expected.Gpu))
            throw new InvalidOperationException(message);
    }
}

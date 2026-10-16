using System.Runtime.Versioning;
using System.Security.Principal;
using Gs66Control.Core;

namespace Gs66Control.Windows;

/// <summary>
/// Restricted transactional access to the reviewed legacy GS66 fan curve
/// layout. The class intentionally has no generic WMI class/property/index API.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCurveControlBackend
{
    public WindowsCurveRuntimeSession OpenReadSession(CancellationToken token = default)
    {
        var verified = CaptureBaseline(token);
        return new WindowsCurveRuntimeSession(verified.Source);
    }

    public FanCurveControlBaseline CaptureBaseline(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var saved = new WindowsSavedCurveProbe().Capture(token);
        var baseline = FanCurveControlBaseline.FromSaved(saved);
        FanCurveControlPolicy.Require(baseline, baseline.Cpu, baseline.Gpu, false, DateTimeOffset.UtcNow);
        return baseline;
    }

    public void SetAutomatic(byte expectedMode, CancellationToken token = default)
    {
        EnsureAdministrator();
        RequireReviewedMode(expectedMode);
        var before = CaptureBaseline(token);
        if (before.Mode != expectedMode)
            throw new InvalidOperationException("Fan mode changed before the requested automatic transition.");

        if (expectedMode != LegacyFanCurveBinding.ReviewedAutomaticMode)
            RestrictedMsiPropertyWriter.UpdateMode(
                expectedMode,
                LegacyFanCurveBinding.ReviewedAutomaticMode,
                token);

        VerifyMode(LegacyFanCurveBinding.ReviewedAutomaticMode, token);
    }

    public void ActivateCustom(byte expectedMode, CancellationToken token = default)
    {
        EnsureAdministrator();
        RequireReviewedMode(expectedMode);
        var before = CaptureBaseline(token);
        if (before.Mode != expectedMode)
            throw new InvalidOperationException("Fan mode changed before the requested custom transition.");

        var targetMode = LegacyFanCurveBinding.ActivateCustomMode(expectedMode);
        if (targetMode != LegacyFanCurveBinding.ReviewedCustomMode)
            throw new InvalidOperationException("The reviewed custom curve mode could not be derived.");
        if (expectedMode != targetMode)
            RestrictedMsiPropertyWriter.UpdateMode(expectedMode, targetMode, token);

        VerifyMode(targetMode, token);
    }

    public void SetSpeed(
        FanCurveChannel channel,
        FanCurvePoint point,
        int expectedSpeedPercent,
        int targetSpeedPercent,
        CancellationToken token = default)
    {
        EnsureAdministrator();
        ValidateSpeed(expectedSpeedPercent, nameof(expectedSpeedPercent));
        ValidateSpeed(targetSpeedPercent, nameof(targetSpeedPercent));

        var before = CaptureBaseline(token);
        var current = Speed(before, channel, point);
        if (current != expectedSpeedPercent)
            throw new InvalidOperationException("The selected fan speed changed before Put; refusing to overwrite unknown state.");

        var cpu = before.Cpu.ToArray();
        var gpu = before.Gpu.ToArray();
        var replacement = new CurvePoint(PointTemperature(before, channel, point), targetSpeedPercent);
        if (channel == FanCurveChannel.Cpu) cpu[(int)point] = replacement;
        else if (channel == FanCurveChannel.Gpu) gpu[(int)point] = replacement;
        else throw new ArgumentOutOfRangeException(nameof(channel));

        FanCurveControlPolicy.Require(before, cpu, gpu, false, DateTimeOffset.UtcNow);
        if (targetSpeedPercent != expectedSpeedPercent)
        {
            RestrictedMsiPropertyWriter.UpdateSpeed(
                channel,
                point,
                checked((byte)expectedSpeedPercent),
                checked((byte)targetSpeedPercent),
                token);
        }

        var after = CaptureBaseline(token);
        if (Speed(after, channel, point) != targetSpeedPercent)
            throw new IOException("Fan speed read-back does not match the requested value.");
    }

    /// <summary>
    /// Restores a previously captured full baseline only when the live device
    /// still exactly matches the caller's expected current state. Any external
    /// change aborts the operation instead of overwriting it.
    /// </summary>
    public void RecoverBaseline(
        FanCurveControlBaseline baseline,
        FanCurveControlBaseline expectedCurrent,
        CancellationToken token = default,
        Action<FanCurveChannel, FanCurvePoint, int, int, bool>? speedMutationObserver = null,
        Action<byte, byte, bool>? modeMutationObserver = null)
    {
        EnsureAdministrator();
        RequireReviewedMode(expectedCurrent.Mode);
        if (baseline.IsSimulated || string.IsNullOrWhiteSpace(baseline.Source) ||
            !LegacyFanCurveBinding.IsReviewedMode(baseline.Mode))
            throw new InvalidOperationException("The recovery baseline is not trusted hardware evidence.");
        var live = CaptureBaseline(token);
        RequireSameState(live, expectedCurrent, "The device changed before recovery started.");
        // The original baseline may be older than the short admission window by
        // the time a session ends. Validate its shape and fixed temperatures
        // against this fresh binding instead of relabelling the old capture.
        FanCurveControlPolicy.Require(live, baseline.Cpu, baseline.Gpu, false, DateTimeOffset.UtcNow);

        var working = expectedCurrent;
        if (baseline.Mode == LegacyFanCurveBinding.ReviewedAutomaticMode &&
            working.Mode != baseline.Mode)
        {
            var beforeMode = working.Mode;
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, false);
            RestrictedMsiPropertyWriter.UpdateMode(working.Mode, baseline.Mode, token);
            working = working with { Mode = baseline.Mode };
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, true);
            var afterMode = CaptureBaseline(token);
            RequireSameState(afterMode, working, "The device changed before curve recovery.");
            working = afterMode;
        }
        foreach (var channel in new[] { FanCurveChannel.Cpu, FanCurveChannel.Gpu })
        {
            var ordered = Enumerable.Range(0, LegacyFanCurveBinding.PointCount)
                .Select(index => (Point: (FanCurvePoint)index,
                    Before: Speed(working, channel, (FanCurvePoint)index),
                    Target: Speed(baseline, channel, (FanCurvePoint)index)))
                .Where(change => change.Before != change.Target)
                .OrderByDescending(change => change.Target > change.Before)
                .ThenBy(change => change.Target > change.Before ? -((int)change.Point) : (int)change.Point)
                .ToArray();
            foreach (var change in ordered)
            {
                var point = change.Point;
                live = CaptureBaseline(token);
                RequireSameState(live, working, "The device changed during curve recovery.");
                var beforeSpeed = Speed(working, channel, point);
                var targetSpeed = Speed(baseline, channel, point);
                if (beforeSpeed == targetSpeed) continue;

                speedMutationObserver?.Invoke(channel, point, beforeSpeed, targetSpeed, false);
                RestrictedMsiPropertyWriter.UpdateSpeed(
                    channel,
                    point,
                    checked((byte)beforeSpeed),
                    checked((byte)targetSpeed),
                    token);
                working = ReplaceSpeed(working, channel, point, targetSpeed);
                speedMutationObserver?.Invoke(channel, point, beforeSpeed, targetSpeed, true);
            }
        }

        live = CaptureBaseline(token);
        RequireSameState(live, working, "The device changed before mode recovery.");
        if (working.Mode != baseline.Mode)
        {
            var beforeMode = working.Mode;
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, false);
            RestrictedMsiPropertyWriter.UpdateMode(working.Mode, baseline.Mode, token);
            working = working with { Mode = baseline.Mode };
            modeMutationObserver?.Invoke(beforeMode, baseline.Mode, true);
        }

        var after = CaptureBaseline(token);
        RequireSameState(after, baseline, "Curve recovery read-back does not match the captured baseline.");
    }

    private static FanCurveControlBaseline ReplaceSpeed(
        FanCurveControlBaseline state,
        FanCurveChannel channel,
        FanCurvePoint point,
        int speed)
    {
        var cpu = state.Cpu.ToArray();
        var gpu = state.Gpu.ToArray();
        if (channel == FanCurveChannel.Cpu)
            cpu[(int)point] = cpu[(int)point] with { Speed = speed };
        else if (channel == FanCurveChannel.Gpu)
            gpu[(int)point] = gpu[(int)point] with { Speed = speed };
        else
            throw new ArgumentOutOfRangeException(nameof(channel));
        return state with { Cpu = cpu, Gpu = gpu };
    }

    private static int PointTemperature(FanCurveControlBaseline state, FanCurveChannel channel, FanCurvePoint point) =>
        channel switch
        {
            FanCurveChannel.Cpu => state.Cpu[(int)point].Temperature,
            FanCurveChannel.Gpu => state.Gpu[(int)point].Temperature,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };

    private static int Speed(FanCurveControlBaseline state, FanCurveChannel channel, FanCurvePoint point) =>
        channel switch
        {
            FanCurveChannel.Cpu => state.Cpu[(int)point].Speed,
            FanCurveChannel.Gpu => state.Gpu[(int)point].Speed,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };

    private static void VerifyMode(byte expected, CancellationToken token)
    {
        var actual = RestrictedMsiPropertyWriter.ReadMode(token);
        if (actual != expected)
            throw new IOException($"Fan mode read-back does not match the requested mode {expected:X2}.");
    }

    private static void RequireSameState(
        FanCurveControlBaseline actual,
        FanCurveControlBaseline expected,
        string message)
    {
        if (actual.IsSimulated != expected.IsSimulated || !string.Equals(actual.Source, expected.Source, StringComparison.Ordinal) ||
            actual.Mode != expected.Mode ||
            !actual.Cpu.SequenceEqual(expected.Cpu) || !actual.Gpu.SequenceEqual(expected.Gpu))
            throw new InvalidOperationException(message);
    }

    private static void RequireReviewedMode(byte mode)
    {
        if (!LegacyFanCurveBinding.IsReviewedMode(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), "Only 0x0D and 0x8D are supported.");
    }

    private static void ValidateSpeed(int speed, string parameter)
    {
        if (speed is < 0 or > LegacyFanCurveBinding.MaximumUiSpeedPercent)
            throw new ArgumentOutOfRangeException(parameter, "Fan speeds must be 0–100 percent.");
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Curve writes require an explicitly elevated helper.");
    }
}

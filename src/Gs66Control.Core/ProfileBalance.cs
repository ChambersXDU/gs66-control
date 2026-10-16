namespace Gs66Control.Core;

public static class ProfileBalance
{
    public static ConsoleProfile Create(string name, IReadOnlyList<CurvePoint> cpu,
        IReadOnlyList<CurvePoint> gpu, int position)
    {
        if (position is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(position));
        var errors = (ConsoleSettings.Default with { Profiles = [new(name, cpu, gpu)] }).Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
        var delta = (int)Math.Round((position - 50) / 5d, MidpointRounding.AwayFromZero);
        CurvePoint[] Adjust(IReadOnlyList<CurvePoint> points)
        {
            var previous = 0;
            return points.Select(point =>
            {
                var minimum = point.Temperature >= LegacyFanCurveBinding.HighTemperatureThresholdCelsius
                    ? LegacyFanCurveBinding.HighTemperatureMinimumSpeedPercent : 0;
                var speed = Math.Max(previous, Math.Clamp(point.Speed + delta, minimum, 100));
                previous = speed;
                return point with { Speed = speed };
            }).ToArray();
        }
        return new(name, Adjust(cpu), Adjust(gpu))
        {
            BalancePosition = position,
            PerformanceShift = null
        };
    }
}

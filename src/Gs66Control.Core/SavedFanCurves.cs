namespace Gs66Control.Core;

public sealed record SavedFanCurves(bool IsSimulated, DateTimeOffset CapturedAtUtc, byte Mode,
    IReadOnlyList<CurvePoint> Cpu, IReadOnlyList<CurvePoint> Gpu, string Source)
{
    public static IReadOnlyList<CurvePoint> ParseLegacyBytes(IReadOnlyList<byte> values, int expectedCount)
    {
        if (expectedCount is not (18 or 19) || values.Count != expectedCount)
            throw new InvalidDataException("Legacy curve instance count is not verified.");
        var points = Enumerable.Range(0, 6).Select(i => new CurvePoint(values[i + 4], values[i + 11])).ToArray();
        var errors = (ConsoleSettings.Default with { Profiles = [new("read-only", points, points)] }).Validate();
        if (errors.Count > 0) throw new InvalidDataException("Device curve cannot be represented without changing its values: " + string.Join(" ", errors));
        return points;
    }
}

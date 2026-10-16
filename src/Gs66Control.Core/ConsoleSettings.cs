using System.Text.Json;

namespace Gs66Control.Core;

public sealed record CurvePoint(int Temperature, int Speed);
public sealed record ConsoleProfile(string Name, IReadOnlyList<CurvePoint> Cpu, IReadOnlyList<CurvePoint> Gpu)
{
    public byte? PerformanceShift { get; init; }
    public int? BalancePosition { get; init; }
}
public sealed record ConsoleSettings(int RefreshSeconds, bool CloseToTray, string Accent, IReadOnlyList<ConsoleProfile> Profiles)
{
    public static ConsoleSettings Default => new(3, true, "#63D7C5", []);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (RefreshSeconds is < 2 or > 30) errors.Add("刷新间隔必须为 2–30 秒。");
        if (Accent is not ("#63D7C5" or "#78A9FF" or "#C29CFF")) errors.Add("请选择可用的强调色。");
        if (Profiles is null || Profiles.Count > 30) return [.. errors, "方案数量必须为 0–30 个。"];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 40 || !names.Add(profile.Name.Trim()))
            {
                errors.Add("方案名称必须唯一且为 1–40 个字符。");
                continue;
            }
            ValidateCurve(profile.Cpu, "CPU", errors);
            ValidateCurve(profile.Gpu, "GPU", errors);
            if (profile.PerformanceShift is not (null or 0xC0 or 0xC1 or 0xC2 or 0xC4))
                errors.Add("性能档位不在已核对范围内。");
            if (profile.BalancePosition is < 0 or > 100)
                errors.Add("方案倾向必须为 0–100。");
        }
        return errors;
    }

    private static void ValidateCurve(IReadOnlyList<CurvePoint>? points, string label, List<string> errors)
    {
        if (points is null || points.Count != 6) { errors.Add($"{label} 曲线必须有 6 个点。"); return; }
        var previousTemperature = -1;
        var previousSpeed = -1;
        foreach (var point in points)
        {
            var firstZero = previousTemperature == -1 && point?.Temperature == 0;
            if (point is null || (!firstZero && (point.Temperature is < 20 or > 100)) || point.Speed is < 0 or > 100 ||
                point.Temperature <= previousTemperature || point.Speed < previousSpeed)
            {
                errors.Add($"{label} 温度须递增且为 20–100（首点可为原厂值 0），速度须不下降且为 0–100。");
                return;
            }
            previousTemperature = point.Temperature;
            previousSpeed = point.Speed;
        }
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConsoleSettings Load()
    {
        if (!File.Exists(path)) return ConsoleSettings.Default;
        var settings = JsonSerializer.Deserialize<ConsoleSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("设置文件为空。");
        var errors = settings.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        return settings;
    }

    public void Save(ConsoleSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

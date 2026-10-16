using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Gs66Control.Windows;

public sealed record VendorScenarioSnapshot(int? Mode, DateTimeOffset CapturedAtUtc)
{
    public string DisplayName => Mode switch { 2 => "平衡", 3 => "静音", 4 => "节能", 5 => "自定义", _ => "未识别" };
}

/// <summary>Only reads the saved scenario; it never changes vendor settings or firmware.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsVendorScenarioProbe
{
    public static VendorScenarioSnapshot Capture()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = root.OpenSubKey(@"SOFTWARE\MSI\One Dragon Center\Component\Base Module\User Scenario");
        var value = key?.GetValue("Mode");
        if (value is not null && (value is not int mode || mode is < 0 or > 5))
            throw new InvalidDataException("微星保存的场景值无效，未猜测当前模式。");
        return new(value as int?, DateTimeOffset.UtcNow);
    }
}

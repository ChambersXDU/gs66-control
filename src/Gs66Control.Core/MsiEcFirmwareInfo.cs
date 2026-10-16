using System.Text;
using System.Text.RegularExpressions;

namespace Gs66Control.Core;

public sealed record MsiEcFirmwareInfo(string Version, string BuildText, string Source);

public static class MsiFirmwareParser
{
    public static MsiEcFirmwareInfo? ParseSoftwareBytes(IReadOnlyList<byte> values)
    {
        if (values.Count != 34) return null;
        var payload = values.Skip(6).ToArray();
        if (payload.Any(value => value is < 32 or > 126)) return null;
        var text = Encoding.ASCII.GetString(payload);
        var version = text[..12];
        var build = text[12..];
        if (!Regex.IsMatch(version, @"\A16V1EMS1\.[0-9A-F]{3}\z") ||
            !Regex.IsMatch(build, @"\A[0-9]{8}[0-9]{2}:[0-9]{2}:[0-9]{2}\z")) return null;
        return new(version, build, "root\\WMI MSI_Software indices 6–33 (reported firmware tag)");
    }
}

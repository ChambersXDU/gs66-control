using System.Text;
using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class MsiFirmwareParserTests
{
    private static byte[] Sample(string payload = "16V1EMS1.1090311202011:16:49") => [1, 3, 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(payload)];

    [Fact]
    public void PreservesReportedTagAndUninterpretedBuildText()
    {
        var parsed = MsiFirmwareParser.ParseSoftwareBytes(Sample());
        Assert.NotNull(parsed);
        Assert.Equal("16V1EMS1.109", parsed.Version);
        Assert.Equal("0311202011:16:49", parsed.BuildText);
        Assert.Contains("MSI_Software", parsed.Source);
    }

    [Fact]
    public void RejectsTruncatedProviderData() =>
        Assert.Null(MsiFirmwareParser.ParseSoftwareBytes(Sample()[..^1]));

    [Fact]
    public void RejectsDifferentDeviceFamily() =>
        Assert.Null(MsiFirmwareParser.ParseSoftwareBytes(Sample("17X1EMS1.1090311202011:16:49")));

    [Fact]
    public void RejectsNonAsciiVersionData()
    {
        var values = Sample(); values[6] = 255;
        Assert.Null(MsiFirmwareParser.ParseSoftwareBytes(values));
    }

    [Fact]
    public void DoesNotGuessMalformedBuildData() =>
        Assert.Null(MsiFirmwareParser.ParseSoftwareBytes(Sample("16V1EMS1.109xxxxxxxx11:16:49")));
}

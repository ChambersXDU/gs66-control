using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class DiagnosticMetadataTests
{
    [Theory]
    [InlineData("MSI_CPU", true)]
    [InlineData("msi_acpi", true)]
    [InlineData("MSiSCSI_Eventlog", false)]
    [InlineData("ISR_MSI", false)]
    [InlineData("MSIde", false)]
    public void SeparatesMsiInterfacesFromWindowsStorageClasses(string name, bool expected) =>
        Assert.Equal(expected, DiagnosticMetadata.IsMsiClass(name));

    [Theory]
    [InlineData("255", "255")]
    [InlineData("1", "255")]
    [InlineData(null, "1")]
    [InlineData("256", "1")]
    [InlineData("-1", "1")]
    [InlineData("unknown", "1")]
    public void DoesNotInventUnknownEcVersion(string? major, string? minor) =>
        Assert.Null(DiagnosticMetadata.ParseEcVersion(major, minor));

    [Fact]
    public void PreservesReportedEcComponents() =>
        Assert.Equal(new EmbeddedControllerVersion(0, 18), DiagnosticMetadata.ParseEcVersion("0", "18"));
}

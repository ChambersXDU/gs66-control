using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class SavedFanCurvesTests
{
    private static byte[] Values()
    {
        var bytes = new byte[19];
        new byte[] { 0, 50, 60, 70, 80, 90 }.CopyTo(bytes, 4);
        new byte[] { 0, 40, 50, 65, 80, 95 }.CopyTo(bytes, 11);
        return bytes;
    }

    [Fact]
    public void PreservesZeroFirstPointAndExactDeviceValues()
    {
        var points = SavedFanCurves.ParseLegacyBytes(Values(), 19);
        Assert.Equal(new CurvePoint(0, 0), points[0]);
        Assert.Equal(new CurvePoint(70, 65), points[3]);
        Assert.Equal(new CurvePoint(90, 95), points[5]);
    }

    [Theory]
    [InlineData(4, 255)]
    [InlineData(5, 0)]
    [InlineData(5, 19)]
    [InlineData(6, 50)]
    [InlineData(12, 101)]
    [InlineData(14, 49)]
    public void RejectsUnrepresentableValuesInsteadOfClamping(int index, byte value)
    {
        var bytes = Values(); bytes[index] = value;
        Assert.Throws<InvalidDataException>(() => SavedFanCurves.ParseLegacyBytes(bytes, 19));
    }

    [Fact]
    public void RejectsChangedInstanceShape() => Assert.Throws<InvalidDataException>(() => SavedFanCurves.ParseLegacyBytes(Values(), 18));
}

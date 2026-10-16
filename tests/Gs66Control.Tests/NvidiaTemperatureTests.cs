using Gs66Control.Core;
using Xunit;

namespace Gs66Control.Tests;

public sealed class NvidiaTemperatureTests
{
    [Fact]
    public void AcceptsOneExpectedDeviceWithCelsiusReading() =>
        Assert.Equal(42, NvidiaTemperature.ParseSingleGs66Gpu(" NVIDIA GeForce RTX 2060, 42\r\n"));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 2060, N/A")]
    [InlineData("NVIDIA GeForce RTX 2060, 0")]
    [InlineData("NVIDIA GeForce RTX 2060, 255")]
    [InlineData("NVIDIA GeForce RTX 2060, 42 C")]
    [InlineData("NVIDIA GeForce RTX 4060, 42")]
    [InlineData("NVIDIA GeForce RTX 2060, 42\nNVIDIA GeForce RTX 2060, 43")]
    [InlineData("NVIDIA GeForce RTX 2060, 42, unexpected")]
    [InlineData("")]
    public void RejectsUnavailableWrongAmbiguousAndMalformedReadings(string output) =>
        Assert.Null(NvidiaTemperature.ParseSingleGs66Gpu(output));
}

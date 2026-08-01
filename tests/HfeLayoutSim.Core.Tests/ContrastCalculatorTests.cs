using HfeLayoutSim.Core.Engine;

namespace HfeLayoutSim.Core.Tests;

public class ContrastCalculatorTests
{
    /// <summary>SPEC §6.1 verification cases, ±0.02 tolerance.</summary>
    [Theory]
    [InlineData("#212529", "#FFFFFF", 15.43)]
    [InlineData("#1565C0", "#FFFFFF", 5.75)]
    [InlineData("#2E7D32", "#FFFFFF", 5.13)]
    [InlineData("#C62828", "#FFFFFF", 5.62)]
    [InlineData("#B45309", "#FFFFFF", 5.02)]
    [InlineData("#F57C00", "#FFFFFF", 2.70)]
    [InlineData("#868E96", "#FFFFFF", 3.32)]
    public void Ratio_MatchesSpecVerificationCases(string fg, string bg, double expected)
    {
        var cr = ContrastCalculator.Ratio(fg, bg);
        Assert.NotNull(cr);
        Assert.InRange(cr!.Value, expected - 0.02, expected + 0.02);
    }

    [Fact]
    public void Ratio_IsSymmetric()
    {
        var a = ContrastCalculator.Ratio("#212529", "#FFFFFF");
        var b = ContrastCalculator.Ratio("#FFFFFF", "#212529");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Ratio_ReturnsNullForUnparsableColor()
        => Assert.Null(ContrastCalculator.Ratio("nope", "#FFFFFF"));

    [Fact]
    public void RelativeLuminance_WhiteIsOne_BlackIsZero()
    {
        Assert.Equal(1.0, ContrastCalculator.RelativeLuminance(new Rgb(255, 255, 255)), 3);
        Assert.Equal(0.0, ContrastCalculator.RelativeLuminance(new Rgb(0, 0, 0)), 3);
    }
}

public class ColorUtilTests
{
    [Theory]
    [InlineData("#212529", true)]
    [InlineData("212529", true)]
    [InlineData("#FFF", true)]
    [InlineData("", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    public void TryParse_HandlesFormats(string input, bool ok)
        => Assert.Equal(ok, ColorUtil.TryParse(input, out _));

    [Theory]
    [InlineData("#C62828", true)]   // red
    [InlineData("#212529", false)]  // near-black
    [InlineData("#868E96", false)]  // neutral gray
    [InlineData("#FFFFFF", false)]
    public void IsChromatic_DistinguishesHueFromNeutral(string hex, bool expected)
        => Assert.Equal(expected, ColorUtil.IsChromatic(hex));

    [Theory]
    [InlineData("#C62828", ColorUtil.HueFamily.Red)]
    [InlineData("#2E7D32", ColorUtil.HueFamily.Green)]
    [InlineData("#1565C0", ColorUtil.HueFamily.Other)]
    [InlineData("#212529", ColorUtil.HueFamily.Other)]
    public void ClassifyHue_RedGreen(string hex, ColorUtil.HueFamily expected)
        => Assert.Equal(expected, ColorUtil.ClassifyHue(hex));
}

public class UnitConverterTests
{
    [Fact]
    public void MmToPx_At96Dpi()
        => Assert.Equal(3.7795, UnitConverter.MmToPx(1), 3);

    [Fact]
    public void PtToPx_At96Dpi()
        => Assert.Equal(16.0, UnitConverter.PtToPx(12), 6);

    [Fact]
    public void Roundtrips()
    {
        Assert.Equal(42.0, UnitConverter.PxToMm(UnitConverter.MmToPx(42)), 9);
        Assert.Equal(10.5, UnitConverter.PxToPt(UnitConverter.PtToPx(10.5)), 9);
        Assert.Equal(12.0, UnitConverter.MmToPt(UnitConverter.PtToMm(12)), 9);
    }
}

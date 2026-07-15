using NcsScheduler.Helpers;

namespace NcsScheduler.Tests.Helpers;

public class BandHelperTests
{
    [Theory]
    [InlineData("10m", 10)]
    [InlineData("80m", 80)]
    [InlineData("160m", 160)]
    [InlineData("2m", 2)]
    public void SortKey_ExtractsLeadingDigitsFromBandName(string band, int expected)
    {
        Assert.Equal(expected, BandHelper.SortKey(band));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Other")]
    public void SortKey_ReturnsMaxValue_ForNoNumericPrefix(string? band)
    {
        Assert.Equal(int.MaxValue, BandHelper.SortKey(band));
    }

    [Fact]
    public void SortKey_OrdersBandsByWavelengthShortestToLongest()
    {
        var bands = new[] { "80m", "10m", "Other", "160m", "2m" };

        var sorted = bands.OrderBy(BandHelper.SortKey).ToArray();

        Assert.Equal(new[] { "2m", "10m", "80m", "160m", "Other" }, sorted);
    }
}

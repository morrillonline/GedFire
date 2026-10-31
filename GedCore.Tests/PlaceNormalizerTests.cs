using GedCore.Matching;

namespace GedCore.Tests;

public class PlaceNormalizerTests
{
    [Theory]
    [InlineData("Edinburg, Christian County, Illinois", "EDINBURG|CHRISTIAN|ILLINOIS")]
    [InlineData("Edinburg, Christian Co., Illinois", "EDINBURG|CHRISTIAN|ILLINOIS")]
    [InlineData("Baton Rouge, East Baton Rouge Parish, Louisiana", "BATON ROUGE|EAST BATON ROUGE|LOUISIANA")]
    [InlineData("Angelus-Rosedale  Cemetery, Los Angeles.", "ANGELUS ROSEDALE CEMETERY|LOS ANGELES")]
    [InlineData("County, Ireland", "COUNTY|IRELAND")]
    [InlineData("St. Louis, Mo.", "ST. LOUIS|MO")]
    [InlineData(" , Maine, ", "MAINE")]
    public void Jurisdictions_IgnoreCountyHyphenSpacingAndTrailingPeriods(string place, string expected) =>
        Assert.Equal(expected, string.Join('|', PlaceNormalizer.Jurisdictions(place)));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ABlankPlace_HasNoJurisdictions(string? place) =>
        Assert.Empty(PlaceNormalizer.Jurisdictions(place));

    [Fact]
    public void ForMatching_ReadsCountyAndNoCountyAlike() =>
        Assert.Equal(
            PlaceNormalizer.ForMatching("Edinburg, Christian, Illinois"),
            PlaceNormalizer.ForMatching("Edinburg, Christian County, Illinois"));
}

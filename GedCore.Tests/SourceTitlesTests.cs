using GedCore.Drafting;

namespace GedCore.Tests;

public class SourceTitlesTests
{
    [Theory]
    [InlineData("U.S., Social Security Death Index, 1935-2014", "u s social security death index 1935 2014")]
    [InlineData("  Find   a Grave!! ", "find a grave")]
    [InlineData("", "")]
    public void Identity_IgnoresCaseAndPunctuationNoise(string title, string expected) =>
        Assert.Equal(expected, SourceTitles.Identity(title));

    [Theory]
    [InlineData("U.S., Social Security Death Index, 1935-2014 - Harry Morrill", "U.S., Social Security Death Index, 1935-2014")]
    [InlineData("1950 United States Federal Census \u2013 Household of Harry Morrill", "1950 United States Federal Census")]
    [InlineData("Find a Grave Index \u2014 Mary-Jane O'Neil", "Find a Grave Index")]
    [InlineData("Collection - Series - Harry Morrill", "Collection - Series")]
    public void Collection_StripsATrailingPersonName(string title, string expected) =>
        Assert.Equal(expected, SourceTitles.Collection(title));

    [Theory]
    [InlineData("Find a Grave")]
    [InlineData("U.S., Social Security Death Index, 1935-2014")]
    [InlineData("Federal Census - 1950")]
    [InlineData("Federal Census - Enumeration District 12")]
    [InlineData("Find a Grave -")]
    public void Collection_KeepsATitleWithoutANameSegment(string title) =>
        Assert.Equal(title, SourceTitles.Collection(title));

    [Fact]
    public void SameCollection_ComparesByIdentity()
    {
        Assert.True(SourceTitles.SameCollection("Find a Grave", "find  a grave."));
        Assert.False(SourceTitles.SameCollection("Social Security Death Index, 1935-2014", "Social Security Death Index, 1935-2013"));
    }
}

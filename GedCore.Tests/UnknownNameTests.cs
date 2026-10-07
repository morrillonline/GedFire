using GedCore.Matching;

namespace GedCore.Tests;

public class UnknownNameTests
{
    [Theory]
    [InlineData("UNKNOWN", true)]
    [InlineData("____", true)]
    [InlineData("SMITH", false)]
    [InlineData("UNKNOWNSON", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPlaceholder_RecognizesTheNormalizedUnknownSpellings(string? part, bool expected)
    {
        Assert.Equal(expected, UnknownName.IsPlaceholder(part));
    }
}

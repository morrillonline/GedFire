using GedCore.Matching;

namespace GedCore.Tests;

public class UnknownNameTests
{
    [Theory]
    [InlineData("UNKNOWN", true)]
    [InlineData("____", true)]
    [InlineData("SMITH", false)]
    [InlineData("UNKNOWNSON", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsPlaceholder_RecognizesUnknownAndEmptyNormalizedParts(string? part, bool expected)
    {
        Assert.Equal(expected, UnknownName.IsPlaceholder(part));
    }
}

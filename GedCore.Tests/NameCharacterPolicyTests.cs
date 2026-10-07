using GedCore.Validate;

namespace GedCore.Tests;

public class NameCharacterPolicyTests
{
    [Theory]
    [InlineData("Renée H. O'Brien-Müller /Søren/ Jr.")]
    [InlineData("John /Smith/ 3rd")]
    [InlineData("John /Smith/ 22nd")]
    [InlineData("John /Smith/, Jr.")]
    [InlineData("Dave /O’Neil/")]
    [InlineData("Mary–Jane /Smith‐Jones/")]
    [InlineData("Cal·la /Puig/")]
    [InlineData("ジョン・ /スミス/")]
    [InlineData("José /Garcı́a/")]
    [InlineData("आशा /शर्मा/")]
    [InlineData("مریم‌ /احمدی/")]
    [InlineData("Unknown /Unknown/")]
    public void DisallowedCharacters_AllowedNames_ReportsNothing(string name)
    {
        Assert.Empty(NameCharacterPolicy.DisallowedCharacters(name));
    }

    [Theory]
    [InlineData("John3 /Smith/", "3")]
    [InlineData("John /Smith3/", "3")]
    [InlineData("John /Smith/ 3", "3")]
    [InlineData("John /Smith/ 3rds", "3")]
    [InlineData("John_Q /Smith/", "_")]
    [InlineData("John /Smith/ #1", "#")]
    [InlineData("John! /Smith/", "!")]
    [InlineData("John $ /Smith/", "$")]
    [InlineData("John /Smith/ 100%", "%")]
    [InlineData("John /?/", "?")]
    [InlineData("Mary (Polly) /Smith/", "(")]
    [InlineData("John \"Jack\" /Smith/", "\"")]
    [InlineData("Smith, John", ",")]
    [InlineData("John \U0001F600 /Smith/", "\U0001F600")]
    public void DisallowedCharacters_ReportsEachOffendingCharacter(string name, string expected)
    {
        Assert.Contains(expected, NameCharacterPolicy.DisallowedCharacters(name));
    }

    [Fact]
    public void DisallowedCharacters_RepeatedCharacter_IsReportedOnce()
    {
        Assert.Equal(["#"], NameCharacterPolicy.DisallowedCharacters("John## /Smith/"));
    }

    [Theory]
    [InlineData("Mary /----/")]
    [InlineData("---- /Pike/")]
    [InlineData("Mary /--/")]
    [InlineData("Mary /-/")]
    [InlineData("Mary /.../")]
    [InlineData("Mary /Smith/--Jones")]
    [InlineData("Mary /Smith——Jones/")]
    [InlineData("Mary /Smith‐‐Jones/")]
    [InlineData("Mary /'/")]
    public void HasPlaceholderPunctuation_PunctuationStandingInForAName_IsTrue(string name)
    {
        Assert.True(NameCharacterPolicy.HasPlaceholderPunctuation(name));
    }

    [Theory]
    [InlineData("Mary-Jane /O'Brien-Smith/")]
    [InlineData("John /Smith/ Jr.")]
    [InlineData("J. R. /Tolkien/")]
    [InlineData("Mary /Unknown/")]
    public void HasPlaceholderPunctuation_OrdinaryNames_IsFalse(string name)
    {
        Assert.False(NameCharacterPolicy.HasPlaceholderPunctuation(name));
    }
}

using GedCore.Matching;

namespace GedCore.Tests;

public class PersonNameNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Fred", "FRED")]
    [InlineData("Mary Anne", "MARY ANNE")]
    [InlineData("Mary  Anne", "MARY ANNE")]           // repeated spaces collapse
    [InlineData("  Mary Anne  ", "MARY ANNE")]         // boundary spaces trim
    [InlineData("O'Brien", "OBRIEN")]                  // apostrophe dropped, not a boundary
    [InlineData("Smith-Jones", "SMITH-JONES")]         // hyphen preserved within a token
    [InlineData("Mary Smith-Jones", "MARY SMITH-JONES")]
    [InlineData("Van  Der   Berg", "VAN DER BERG")]
    [InlineData("--Smith--Jones--", "SMITH-JONES")]    // repeated + leading/trailing hyphens
    [InlineData("Smith---Jones", "SMITH-JONES")]
    [InlineData("-Fred-", "FRED")]
    [InlineData("-", "")]                              // a lone hyphen normalizes away
    [InlineData("____", "____")]                       // unknown-name placeholder survives
    [InlineData("Jean-Paul", "JEAN-PAUL")]
    [InlineData("O'Brien-Smith Jr.", "OBRIEN-SMITH JR")]
    [InlineData("A1", "A1")]                            // digits kept
    [InlineData("Jean_Paul", "JEAN_PAUL")]              // underscore kept
    [InlineData("José", "JOSÉ")]                        // Unicode letters kept and uppercased
    public void Normalize_ProducesExpected(string? input, string expected)
        => Assert.Equal(expected, PersonNameNormalizer.Normalize(input));

    [Fact]
    public void Normalize_IsIdempotent()
    {
        string once = PersonNameNormalizer.Normalize("Mary Anne O'Brien-Smith, Jr.");
        string twice = PersonNameNormalizer.Normalize(once);
        Assert.Equal(once, twice);
    }
}

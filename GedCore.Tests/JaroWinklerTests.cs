using GedCore.Matching;

namespace GedCore.Tests;

public class JaroWinklerTests
{
    [Fact]
    public void IdenticalStrings_ScoreOne()
        => Assert.Equal(1.0, JaroWinkler.Similarity("FREDERICK", "FREDERICK"));

    [Theory]
    [InlineData(null, "FRED")]
    [InlineData("", "FRED")]
    [InlineData("FRED", null)]
    [InlineData("FRED", "")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void EmptyOrNullInput_ScoresZero(string? a, string? b)
        => Assert.Equal(0.0, JaroWinkler.Similarity(a, b));

    [Fact]
    public void Similarity_IsSymmetric()
    {
        double ab = JaroWinkler.Similarity("MARTHA", "MARHTA");
        double ba = JaroWinkler.Similarity("MARHTA", "MARTHA");
        Assert.Equal(ab, ba);

        double ab2 = JaroWinkler.Similarity("FRED", "FREDERICK");
        double ba2 = JaroWinkler.Similarity("FREDERICK", "FRED");
        Assert.Equal(ab2, ba2);
    }

    [Fact]
    public void Transposition_ScoresBelowOneButAboveHalf()
    {
        // MARTHA/MARHTA differ by one adjacent transposition (T/H); the
        // classic Winkler reference pair.
        double sim = JaroWinkler.Similarity("MARTHA", "MARHTA");
        Assert.True(sim is > 0.9 and < 1.0, $"expected a high but imperfect score, got {sim}");
    }

    [Fact]
    public void CommonPrefixBonus_CapsAtFourCharacters()
    {
        // "ABCDEF" vs "ABCDEF" + one differing trailing character: 6 of 7
        // characters match at the same position, 0 transpositions, and the
        // strings share a 6-character common prefix. The Winkler bonus must
        // behave as though only 4 of those characters counted.
        double actual = JaroWinkler.Similarity("ABCDEFG", "ABCDEFQ");

        double jaroBase = (6.0 / 7 + 6.0 / 7 + 6.0 / 6) / 3.0;
        double expectedWithFourCharCap = jaroBase + 4 * 0.1 * (1 - jaroBase);
        double expectedWithoutCap = jaroBase + 6 * 0.1 * (1 - jaroBase);

        Assert.Equal(expectedWithFourCharCap, actual, 9);
        Assert.True(Math.Abs(expectedWithoutCap - actual) > 1e-6,
            "the prefix bonus must not credit more than 4 shared characters");
    }

    [Theory]
    [InlineData("FRED", "FREDERICK")]
    public void ShortenedForm_ScoresHigh(string a, string b)
    {
        // FRED vs FREDERICK scores about 0.89 — the prefix bonus is what
        // makes a shortened form score high without any lookup table.
        double sim = JaroWinkler.Similarity(a, b);
        Assert.Equal(0.89, sim, 2);
    }

    [Theory]
    [InlineData("BILL", "WILLIAM")]
    public void DocumentedNickname_ScoresLowerThanSpellingVariant(string a, string b)
    {
        // BILL vs WILLIAM scores only about 0.73 — documented nicknames are
        // not a spelling phenomenon.
        double sim = JaroWinkler.Similarity(a, b);
        Assert.Equal(0.73, sim, 2);
        Assert.True(sim < 0.89, "a nickname pair should not out-score a true shortened form");
    }
}

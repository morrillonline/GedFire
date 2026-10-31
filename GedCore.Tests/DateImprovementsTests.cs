using GedCore.Apply;

namespace GedCore.Tests;

public class DateImprovementsTests
{
    [Fact]
    public void NoPriorDate_IsTheFirst() =>
        Assert.Equal(DateImprovement.First, DateImprovements.Classify(null, "1877"));

    [Theory]
    [InlineData("1877", "14 APR 1877")]
    [InlineData("APR 1877", "14 APR 1877")]
    [InlineData("ABT 1877", "1877")]
    [InlineData("BEF 1800", "1797")]
    public void AMoreSpecificConsistentDate_IsRefined(string prior, string written) =>
        Assert.Equal(DateImprovement.Refined, DateImprovements.Classify(prior, written));

    [Theory]
    [InlineData("BEF 1800", "BEF 1797")]
    [InlineData("BEFORE 1800", "BEF 1797")]
    [InlineData("AFT 1795", "BET 1795 AND 1797")]
    [InlineData("BET 1790 AND 1800", "BET 1795 AND 1797")]
    public void ATighterBound_IsNarrowed(string prior, string written) =>
        Assert.Equal(DateImprovement.Narrowed, DateImprovements.Classify(prior, written));

    [Theory]
    [InlineData("17 OCT 1810", "BEF 27 SEP 1796")]
    [InlineData("1877", "1878")]
    [InlineData("14 APR 1877", "15 APR 1877")]
    [InlineData("BEF 1790", "AFT 1795")]
    public void AContradictedDate_IsCorrected(string prior, string written) =>
        Assert.Equal(DateImprovement.Corrected, DateImprovements.Classify(prior, written));

    [Theory]
    [InlineData("14 APR 1877", "1877")]
    [InlineData("1877", "ABT 1877")]
    [InlineData("14 APR 1877", "BEF 1880")]
    [InlineData("BEF 1797", "BEF 1800")]
    public void ALessSpecificConsistentDate_IsNoImprovement(string prior, string written) =>
        Assert.Equal(DateImprovement.None, DateImprovements.Classify(prior, written));

    [Theory]
    [InlineData("BEFORE 1796", "BEF 1796")]
    [InlineData("05 NOV 1874", "5 Nov 1874")]
    [InlineData("ABOUT 1877", "ABT 1877")]
    [InlineData("BETWEEN 01 AUG 1796 AND SEPT 1796", "BET 1 AUG 1796 AND SEP 1796")]
    public void TheSameDateRespelled_IsNoImprovement(string prior, string written) =>
        Assert.Equal(DateImprovement.None, DateImprovements.Classify(prior, written));
}

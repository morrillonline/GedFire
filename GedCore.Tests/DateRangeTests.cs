namespace GedCore.Tests;

public class DateRangeTests
{
    [Theory]
    [InlineData("BET 18 AUG 1796 AND 27 SEP 1796", true)]
    [InlineData("BET 1 JAN 1796 AND 1 JAN 1797", true)]
    [InlineData("BET 1 JAN 1796 AND 2 JAN 1797", false)]
    [InlineData("BET JAN 1796 AND DEC 1796", true)]
    [InlineData("BET 1796 AND 1796", true)]
    [InlineData("BET 1796 AND 1797", false)]
    [InlineData("BETWEEN 18 AUG 1796 AND 27 SEP 1796", true)]
    public void ABetweenRange_FitsWhenItsWidestReadingIsWithinTheSpan(string date, bool expected) =>
        Assert.Equal(expected, DateRange.SpansAtMost(date, 12));

    [Theory]
    [InlineData("BEF 27 SEP 1796")]
    [InlineData("AFT 18 AUG 1796")]
    [InlineData("ABT 1796")]
    [InlineData("14 APR 1796")]
    [InlineData("FROM 1796 TO 1796")]
    [InlineData("BET 31 FEB 1796 AND MAR 1796")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingButAReadableBetweenRange_NeverFits(string? date) =>
        Assert.False(DateRange.SpansAtMost(date, 12));
}

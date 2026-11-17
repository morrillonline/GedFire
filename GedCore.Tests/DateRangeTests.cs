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

    [Theory]
    [InlineData("14 APR 1796", "1796-04-14", "1796-04-14")]
    [InlineData("APR 1796", "1796-04-01", "1796-04-30")]
    [InlineData("1796", "1796-01-01", "1796-12-31")]
    [InlineData("ABT 1796", "1794-01-01", "1798-12-31")]
    [InlineData("EST 14 APR 1796", "1794-04-14", "1798-04-14")]
    [InlineData("BET 1790 AND MAR 1796", "1790-01-01", "1796-03-31")]
    [InlineData("BEF 1796", null, "1796-12-31")]
    [InlineData("AFT 1796", "1796-01-01", null)]
    [InlineData("FROM 1790 TO 1796", "1790-01-01", "1796-12-31")]
    [InlineData("GREGORIAN 1796", "1796-01-01", "1796-12-31")]
    [InlineData("JULIAN 1796", null, null)]
    [InlineData("ABT JULIAN 1796", null, null)]
    [InlineData("BEF JULIAN 1796", null, null)]
    [InlineData("INT 1796 (about then)", null, null)]
    [InlineData("", null, null)]
    public void EarliestAndLatest_AreTheWidestReadingOfTheDate(string date, string? earliest, string? latest)
    {
        Assert.Equal(earliest is null ? null : DateOnly.ParseExact(earliest, "yyyy-MM-dd"), DateRange.Earliest(date));
        Assert.Equal(latest is null ? null : DateOnly.ParseExact(latest, "yyyy-MM-dd"), DateRange.Latest(date));
    }
}

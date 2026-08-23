using GedCore;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// date-calc's engine: GedAge parsing/formatting, GedDate.AddAge/SubtractAge/
// Diff, and GedDate.ResolveDualYear. Program.RunDateCalc (the CLI adapter) is
// exercised separately in CommandLineTests-adjacent coverage; this file is
// the pure engine's own correctness proof.
// ---------------------------------------------------------------------------

public class GedAgeParseTests
{
    [Theory]
    [InlineData("63y 4m 2d", 63, 4, 2)]
    [InlineData("63y", 63, 0, 0)]
    [InlineData("4m 2d", 0, 4, 2)]
    [InlineData("63y 2d", 63, 0, 2)]
    [InlineData("0d", 0, 0, 0)]
    [InlineData("0y 0m 0d", 0, 0, 0)]
    // Leading zeroes are accepted on input.
    [InlineData("007y 04m 02d", 7, 4, 2)]
    public void Parse_ValidGrammar_ProducesExpectedComponents(string text, int y, int m, int d)
    {
        var age = GedAge.Parse(text);
        Assert.Equal(new GedAge(y, m, d), age);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("4m 63y")]        // out of order
    [InlineData("63y 63y")]       // repeated unit
    [InlineData("63y  4m")]       // double space
    [InlineData(" 63y")]          // leading space
    [InlineData("63y ")]          // trailing space
    [InlineData("-1y")]           // sign
    [InlineData("1.5y")]          // decimal
    [InlineData("1w")]            // unknown unit
    [InlineData("y")]             // missing digits
    [InlineData("63")]            // missing unit
    public void Parse_InvalidGrammar_ThrowsFormatException(string text)
    {
        Assert.Throws<FormatException>(() => GedAge.Parse(text));
    }

    [Fact]
    public void Parse_MonthsOutOfRange_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => GedAge.Parse("1y 12m"));
        Assert.Contains("months", ex.Message);
    }

    [Fact]
    public void Parse_DaysOutOfRange_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => GedAge.Parse("1y 31d"));
        Assert.Contains("days", ex.Message);
    }

    [Fact]
    public void Parse_MonthsAtEleven_AndDaysAtThirty_AreValid()
    {
        var age = GedAge.Parse("1y 11m 30d");
        Assert.Equal(new GedAge(1, 11, 30), age);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalseRatherThanThrowing()
    {
        Assert.False(GedAge.TryParse("bogus", out _));
        Assert.False(GedAge.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrueWithComponents()
    {
        Assert.True(GedAge.TryParse("2y 3m", out var age));
        Assert.Equal(new GedAge(2, 3, 0), age);
    }

    [Theory]
    [InlineData(63, 4, 2, "63y 4m 2d")]
    [InlineData(0, 0, 0, "0y 0m 0d")]
    // ToString always prints all three canonical components, no leading zeroes.
    [InlineData(7, 0, 9, "7y 0m 9d")]
    public void ToString_AlwaysPrintsAllThreeComponents(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, new GedAge(y, m, d).ToString());
    }
}

public class GedDateArithmeticTests
{
    // -------------------------------------------------------------------
    // ResolveDualYear (--op normalize)
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("1691/2", 1692)]   // the design's own worked example
    [InlineData("1745/46", 1746)]
    [InlineData("5/6", 6)]         // single-digit years on both sides
    [InlineData("1800", 1800)]     // no slash: passes through unchanged
    public void ResolveDualYear_ResolvesRightHandYear(string input, int expected)
    {
        Assert.Equal(expected, GedDate.ResolveDualYear(input));
    }

    [Theory]
    [InlineData("5/678")]   // right-hand suffix longer than the left year
    [InlineData("/6")]      // missing left year
    [InlineData("5/")]      // missing right-hand suffix
    [InlineData("17a5/46")] // non-digit characters
    public void ResolveDualYear_MalformedInput_Throws(string input)
    {
        Assert.Throws<FormatException>(() => GedDate.ResolveDualYear(input));
    }

    // -------------------------------------------------------------------
    // NormalizeDualDate (--op normalize)
    // -------------------------------------------------------------------

    [Fact]
    public void NormalizeDualDate_WorkedExample_ResolvesTheRightHandYear()
    {
        Assert.Equal("11 FEB 1692", GedDate.NormalizeDualDate("11 FEB 1691/2"));
    }

    [Fact]
    public void NormalizeDualDate_PlainYear_PassesThroughUnchanged()
    {
        Assert.Equal("11 FEB 1780", GedDate.NormalizeDualDate("11 FEB 1780"));
    }

    [Theory]
    [InlineData("1691/2")]                // missing day/month
    [InlineData("11 FEB")]                 // missing year
    [InlineData("ABT 11 FEB 1691/2")]      // qualifier not accepted
    [InlineData("35 FEB 1691/2")]          // day out of range
    [InlineData("11 XYZ 1691/2")]          // unrecognized month
    [InlineData("")]
    public void NormalizeDualDate_OutsideTheExactGrammar_Throws(string input)
    {
        Assert.Throws<FormatException>(() => GedDate.NormalizeDualDate(input));
    }

    // -------------------------------------------------------------------
    // ParseExactGregorianDate (--op add/sub/diff's --date/--from/--to)
    // -------------------------------------------------------------------

    [Fact]
    public void ParseExactGregorianDate_ValidDate_ParsesToExpectedDateTime()
    {
        Assert.Equal(new DateTime(1777, 9, 27), GedDate.ParseExactGregorianDate("27 SEP 1777"));
    }

    [Theory]
    [InlineData("ABT 1780")]           // qualifier
    [InlineData("BEF 1854")]           // qualifier
    [InlineData("1780")]               // year-only, partial
    [InlineData("JUN 1780")]           // month-year only, partial
    [InlineData("15 JUN 1691/2")]      // dual-dated year not accepted here
    [InlineData("30 FEB 1900")]        // no such calendar date
    [InlineData("15 JUN 4000 BCE")]    // BCE epoch
    [InlineData("GREGORIAN 15 JUN 1800")] // calendar prefix
    [InlineData("")]
    public void ParseExactGregorianDate_OutsideTheExactGrammar_Throws(string input)
    {
        Assert.Throws<FormatException>(() => GedDate.ParseExactGregorianDate(input));
    }

    // -------------------------------------------------------------------
    // AddAge / SubtractAge: order of operations and clamp-vs-roll rules
    // -------------------------------------------------------------------

    [Fact]
    public void AddAge_AppliesYearsMonthsDaysInThatOrder()
    {
        // 27 SEP 1777 + 63y 4m 2d = 29 JAN 1841 (the design's own worked
        // example, gravestone birth -> inscribed-age death date).
        var result = GedDate.AddAge(new DateTime(1777, 9, 27), new GedAge(63, 4, 2));
        Assert.Equal(new DateTime(1841, 1, 29), result);
    }

    [Fact]
    public void SubtractAge_IsTheInverseOperationForTheSameWorkedExample()
    {
        // Gravestone back-calculation: death date minus inscribed age.
        var result = GedDate.SubtractAge(new DateTime(1841, 1, 29), new GedAge(63, 4, 2));
        Assert.Equal(new DateTime(1777, 9, 27), result);
    }

    [Fact]
    public void AddAge_YearStep_ClampsFebruary29ToFebruary28()
    {
        var result = GedDate.AddAge(new DateTime(1840, 2, 29), new GedAge(1, 0, 0));
        Assert.Equal(new DateTime(1841, 2, 28), result);
    }

    [Fact]
    public void AddAge_MonthStep_ClampsToTargetMonthsLastValidDay()
    {
        // 31 MAR 1841 + one month = 30 APR 1841 -- not an invalid date, and
        // not a roll-forward into May.
        var result = GedDate.AddAge(new DateTime(1841, 3, 31), new GedAge(0, 1, 0));
        Assert.Equal(new DateTime(1841, 4, 30), result);
    }

    [Fact]
    public void AddAge_DayStep_RollsThroughMonthAndYearBoundaries()
    {
        var result = GedDate.AddAge(new DateTime(1841, 12, 30), new GedAge(0, 0, 5));
        Assert.Equal(new DateTime(1842, 1, 4), result);
    }

    [Fact]
    public void SubtractAge_AppliesNegatedYearMonthDayInTheSameOrder()
    {
        // 30 APR minus one month is 30 MAR (March has 31 days, so no
        // clamping is needed here -- the mirror case, AddAge_MonthStep_
        // ClampsToTargetMonthsLastValidDay above, is where clamping shows).
        var result = GedDate.SubtractAge(new DateTime(1841, 4, 30), new GedAge(0, 1, 0));
        Assert.Equal(new DateTime(1841, 3, 30), result);
    }

    [Fact]
    public void AddAge_ZeroAge_ReturnsSameDate()
    {
        var date = new DateTime(1900, 6, 15);
        Assert.Equal(date, GedDate.AddAge(date, default));
    }

    [Fact]
    public void AddAge_YearOutOfSupportedRange_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GedDate.AddAge(new DateTime(9999, 1, 1), new GedAge(1, 0, 0)));
    }

    // -------------------------------------------------------------------
    // Diff: greedy y/m/d breakdown and its round-trip guarantee with AddAge
    // -------------------------------------------------------------------

    [Fact]
    public void Diff_WorkedExample_MatchesTheAgeUsedToBuildIt()
    {
        // The same 63y 4m 2d age the AddAge/SubtractAge worked examples use,
        // recovered from the two dates that bracket it.
        var age = GedDate.Diff(new DateTime(1777, 9, 27), new DateTime(1841, 1, 29));
        Assert.Equal(new GedAge(63, 4, 2), age);
    }

    [Fact]
    public void Diff_EqualDates_ReturnsAllZeroes()
    {
        var date = new DateTime(1900, 1, 1);
        Assert.Equal(new GedAge(0, 0, 0), GedDate.Diff(date, date));
    }

    [Fact]
    public void Diff_FromAfterTo_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => GedDate.Diff(new DateTime(1900, 1, 2), new DateTime(1900, 1, 1)));
    }

    [Theory]
    [InlineData("1777-09-27", "1841-01-29")]
    [InlineData("1840-02-29", "1841-02-28")]  // leap-day edge
    [InlineData("1900-01-01", "1900-01-01")]  // equal dates
    [InlineData("2000-01-31", "2000-03-01")]  // crosses a short February
    [InlineData("1999-12-31", "2000-01-01")]  // crosses a year boundary by one day
    public void Diff_RoundTripsThroughAddAge_ForAnyOrderedPair(string fromStr, string toStr)
    {
        var from = DateTime.Parse(fromStr, System.Globalization.CultureInfo.InvariantCulture);
        var to = DateTime.Parse(toStr, System.Globalization.CultureInfo.InvariantCulture);

        var age = GedDate.Diff(from, to);
        Assert.Equal(to, GedDate.AddAge(from, age));

        // Every component Diff produces is within the ranges GedAge.Parse
        // itself accepts (months 0-11, days 0-30), so the printed value can
        // always be pasted straight into a later --op add call.
        Assert.InRange(age.Months, 0, 11);
        Assert.InRange(age.Days, 0, 30);
        Assert.True(age.Years >= 0);
    }

    [Fact]
    public void Diff_OutputStringIsValidGedAgeInput()
    {
        var age = GedDate.Diff(new DateTime(1777, 9, 27), new DateTime(1841, 1, 29));
        string printed = age.ToString();
        Assert.Equal("63y 4m 2d", printed);
        Assert.Equal(age, GedAge.Parse(printed));
    }
}

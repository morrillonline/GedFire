using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Edge cases not covered by the original GedDateTests: the short-double-date
/// crash, the dotted ABT variant, and Qualifier recognition.
/// </summary>
public class GedDateEdgeCaseTests
{
    [Fact]
    public void Parse_ShortSlashDate_ReturnsNullInsteadOfThrowing()
    {
        // Regression: "5/6" used to throw ArgumentOutOfRangeException from
        // year[..(slash - 2)].
        Assert.Null(GedDate.Parse("5/6"));
    }

    [Fact]
    public void Parse_DottedAboutQualifier_IsSkipped() =>
        Assert.Equal(new DateTime(1780, 1, 1), GedDate.Parse("ABT. 1780"));

    [Theory]
    [InlineData("ABT 1780",  "ABT")]
    [InlineData("ABOUT 1780", "ABT")]
    [InlineData("BEF 1854",  "BEF")]
    [InlineData("BET 1850 AND 1860", "BET")]
    [InlineData("1 JAN 1800", null)]
    [InlineData("", null)]
    public void Qualifier_RecognizesLeadingToken(string date, string? expected) =>
        Assert.Equal(expected, GedDate.Qualifier(date));
}

// ---------------------------------------------------------------------------
// Subproject C (SPEC-ged7-conformance.md): full GEDCOM 7 DateValue grammar
// via GedDate.ParseValue. One theory per grammar production, plus a
// wrapper-equivalence check that Parse/ParseYear still return exactly what
// they returned before this subproject for every string already exercised
// elsewhere in the suite.
// ---------------------------------------------------------------------------

public class GedDateValueTests
{
    [Theory]
    [InlineData("15 JUN 1800", GedDateKind.Exact, 1800)]
    [InlineData("1800",        GedDateKind.Exact, 1800)]
    [InlineData("986",         GedDateKind.Exact, 986)]   // 1-4 digit year defect fix
    [InlineData("12",          GedDateKind.Exact, 12)]
    [InlineData("7",           GedDateKind.Exact, 7)]
    public void ExactDate_ParsesToExpectedYearAndKind(string date, GedDateKind kind, int year)
    {
        var v = GedDate.ParseValue(date);
        Assert.NotNull(v);
        Assert.Equal(kind, v!.Kind);
        Assert.Equal(year, v.FromYear);
    }

    [Theory]
    [InlineData("ABT 1780",  GedDateKind.About,      1780)]
    [InlineData("ABOUT 1780", GedDateKind.About,     1780)]
    [InlineData("CAL 1780",  GedDateKind.Calculated, 1780)]
    [InlineData("EST 1780",  GedDateKind.Estimated,  1780)]
    public void QualifiedSingleDate_ParsesToExpectedKind(string date, GedDateKind kind, int year)
    {
        var v = GedDate.ParseValue(date);
        Assert.NotNull(v);
        Assert.Equal(kind, v!.Kind);
        Assert.Equal(year, v.FromYear);
        Assert.Equal(new DateTime(year, 1, 1), v.From);
        Assert.Null(v.To);
    }

    [Theory]
    [InlineData("BEF 1854", GedDateKind.Before, 1854)]
    [InlineData("AFT 1854", GedDateKind.After,  1854)]
    public void BeforeAfter_ParsesToExpectedKind(string date, GedDateKind kind, int year)
    {
        var v = GedDate.ParseValue(date);
        Assert.NotNull(v);
        Assert.Equal(kind, v!.Kind);
        Assert.Equal(year, v.FromYear);
    }

    [Fact]
    public void Between_ParsesBothBounds()
    {
        var v = GedDate.ParseValue("BET 1850 AND 1860");
        Assert.NotNull(v);
        Assert.Equal(GedDateKind.Between, v!.Kind);
        Assert.Equal(1850, v.FromYear);
        Assert.Equal(1860, v.ToYear);
        Assert.Equal(new DateTime(1850, 1, 1), v.From);
        Assert.Equal(new DateTime(1860, 1, 1), v.To);
    }

    [Fact]
    public void FromTo_BothSides_ParsesAsPeriod()
    {
        var v = GedDate.ParseValue("FROM 1850 TO 1860");
        Assert.NotNull(v);
        Assert.Equal(GedDateKind.Period, v!.Kind);
        Assert.Equal(1850, v.FromYear);
        Assert.Equal(1860, v.ToYear);
    }

    [Fact]
    public void From_OnlyStart_ParsesAsPeriodWithNullEnd()
    {
        var v = GedDate.ParseValue("FROM 1850");
        Assert.NotNull(v);
        Assert.Equal(GedDateKind.Period, v!.Kind);
        Assert.Equal(1850, v.FromYear);
        Assert.Equal(0, v.ToYear);
        Assert.Null(v.To);
    }

    [Fact]
    public void To_OnlyEnd_ParsesAsPeriodWithNullStart()
    {
        var v = GedDate.ParseValue("TO 1860");
        Assert.NotNull(v);
        Assert.Equal(GedDateKind.Period, v!.Kind);
        Assert.Equal(0, v.FromYear);
        Assert.Equal(1860, v.ToYear);
        Assert.Null(v.From);
    }

    [Theory]
    [InlineData("JULIAN 1700",   1700)]
    [InlineData("FRENCH_R 1793", 1793)]
    [InlineData("HEBREW 5550",   5550)]
    public void NonGregorianCalendar_ParsesYearOnly_NoDateTime(string date, int year)
    {
        var v = GedDate.ParseValue(date);
        Assert.NotNull(v);
        Assert.Equal(year, v!.FromYear);
        Assert.Null(v.From);
    }

    [Fact]
    public void GregorianCalendarKeyword_ParsesNormally()
    {
        var v = GedDate.ParseValue("GREGORIAN 15 JUN 1800");
        Assert.NotNull(v);
        Assert.Equal(1800, v!.FromYear);
        Assert.Equal(new DateTime(1800, 6, 15), v.From);
    }

    [Fact]
    public void Bce_NegatesYear_LeavesDateTimeNull()
    {
        var v = GedDate.ParseValue("4000 BCE");
        Assert.NotNull(v);
        Assert.Equal(-4000, v!.FromYear);
        Assert.Null(v.From);
    }

    [Fact]
    public void Bet_DualYearInsideRange_ResolvesLeftYearAndNormalizedDate()
    {
        var v = GedDate.ParseValue("BET 1745/46 AND 1750");
        Assert.NotNull(v);
        Assert.Equal(GedDateKind.Between, v!.Kind);
        Assert.Equal(1745, v.FromYear);          // FromYear keeps the left/old-style year
        Assert.Equal(1746, v.From!.Value.Year);  // From normalizes to the Gregorian year
        Assert.Equal(1750, v.ToYear);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PRIVATE 1800")]
    [InlineData("5/6")]
    [InlineData("BET 1850")]        // missing AND
    [InlineData("FROM")]            // no date follows
    public void UnparseableInput_ReturnsNull(string? date) =>
        Assert.Null(GedDate.ParseValue(date));

    // -------------------------------------------------------------------
    // Wrapper equivalence: every date string already exercised by
    // GedDateTests/GedDateEdgeCaseTests must still yield the same Parse/
    // ParseYear results now that both delegate to ParseValue.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData(null,          0,    null)]
    [InlineData("",            0,    null)]
    [InlineData("1800",        1800, "1800-01-01")]
    [InlineData("ABT 1800",    1800, "1800-01-01")]
    [InlineData("15 JUN 1800", 1800, "1800-06-15")]
    [InlineData("1745/46",     1745, "1746-01-01")]
    [InlineData("JUN 1800",    1800, "1800-06-01")]
    [InlineData("ABT. 1780",   1780, "1780-01-01")]
    [InlineData("5/6",         0,    null)]
    [InlineData("PRIVATE 1800", 0,   null)]
    public void ParseAndParseYear_MatchPreSubprojectCBehavior(
        string? date, int expectedYear, string? expectedParse)
    {
        Assert.Equal(expectedYear, GedDate.ParseYear(date));
        DateTime? expected = expectedParse is null ? null : DateTime.Parse(expectedParse);
        Assert.Equal(expected, GedDate.Parse(date));
    }
}

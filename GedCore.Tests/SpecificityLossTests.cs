using GedCore.Validate;

namespace GedCore.Tests;

/// <summary>
/// Pure value-comparison tests for <see cref="SpecificityLoss"/>: a proposed
/// place that is a proper suffix of the held hierarchy, or a proposed date
/// that drops the held date's day (or day and month), is less specific; a
/// conflicting value and a more detailed value never are.
/// </summary>
public class SpecificityLossTests
{
    // -------------------------------------------------------------------
    // Places
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Pembroke, Yarmouth, Nova Scotia, Canada", "Canada")]
    [InlineData("Pembroke, Yarmouth, Nova Scotia, Canada", "Nova Scotia, Canada")]
    [InlineData("Pembroke, Yarmouth, Nova Scotia, Canada", "Yarmouth, Nova Scotia, Canada")]
    [InlineData("Boston, Suffolk, Massachusetts", "Massachusetts")]
    [InlineData("Boston, Suffolk, Massachusetts", "suffolk, massachusetts")]   // case-insensitive
    [InlineData("Boston , Suffolk , Massachusetts", "Suffolk, Massachusetts")] // segment trimming
    public void Place_ProperSuffixOfHeldHierarchy_IsLessSpecific(string held, string proposed) =>
        Assert.True(SpecificityLoss.IsLessSpecificPlace(held, proposed));

    [Theory]
    [InlineData("Boston, Massachusetts", "Boston, New Hampshire")]             // conflicting value
    [InlineData("Massachusetts", "Boston, Suffolk, Massachusetts")]            // more detailed
    [InlineData("Boston, Suffolk, Massachusetts", "Suffolk")]                  // inner segment, not a suffix
    [InlineData("Boston, Suffolk, Massachusetts", "Boston")]                   // leading segment, not a suffix
    [InlineData("Pembroke, Yarmouth, Nova Scotia", "Canada")]                  // not in the held hierarchy
    [InlineData("Boston, Massachusetts", "Boston, Massachusetts")]             // equal, not a proper suffix
    [InlineData("Boston,Massachusetts", "Boston, Massachusetts")]              // spacing noise only
    [InlineData("Massachusetts", "")]
    [InlineData("", "Massachusetts")]
    [InlineData(null, "Massachusetts")]
    [InlineData("Massachusetts", null)]
    public void Place_ConflictingMoreDetailedOrNonSuffix_IsNot(string? held, string? proposed) =>
        Assert.False(SpecificityLoss.IsLessSpecificPlace(held, proposed));

    // -------------------------------------------------------------------
    // Dates
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("05 NOV 1874", "1874")]
    [InlineData("05 NOV 1874", "NOV 1874")]
    [InlineData("5 NOV 1874", "1874")]        // unpadded held day
    [InlineData("NOV 1874", "1874")]
    [InlineData("05 NOV 1874", "nov 1874")]   // case-insensitive month
    public void Date_TruncationOfHeldDate_IsLessSpecific(string held, string proposed) =>
        Assert.True(SpecificityLoss.IsLessSpecificDate(held, proposed));

    [Theory]
    [InlineData("1874", "05 NOV 1874")]              // more detailed
    [InlineData("NOV 1874", "05 NOV 1874")]          // more detailed
    [InlineData("05 NOV 1874", "1875")]              // conflicting year
    [InlineData("05 NOV 1874", "DEC 1874")]          // conflicting month
    [InlineData("05 NOV 1874", "04 NOV 1874")]       // conflicting day, same length
    [InlineData("1874", "1874")]                     // equal
    [InlineData("ABT 1874", "1874")]                 // qualified held date never triggers
    [InlineData("BET 1870 AND 1874", "1874")]        // ranged held date never triggers
    [InlineData("05 NOV 1874", "ABT 1874")]          // qualified proposal never triggers
    [InlineData("FROM 1870 TO 1874", "1874")]        // period never triggers
    [InlineData("1745/46", "1745")]                  // dual year never triggers
    [InlineData("05 NOV 1874", "")]
    [InlineData("", "1874")]
    [InlineData(null, "1874")]
    [InlineData("05 NOV 1874", null)]
    public void Date_ConflictingQualifiedOrMoreDetailed_IsNot(string? held, string? proposed) =>
        Assert.False(SpecificityLoss.IsLessSpecificDate(held, proposed));
}

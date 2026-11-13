namespace GedCore.Tests;

public class GedDateSyntaxTests
{
    [Theory]
    [InlineData("1840")]
    [InlineData("JUN 1840")]
    [InlineData("1 JUN 1840")]
    [InlineData("1 jun 1840")]
    [InlineData("29 FEB 1840")]
    [InlineData("ABT 1840")]
    [InlineData("CAL 1 JUN 1840")]
    [InlineData("EST 1840")]
    [InlineData("BEF 1840")]
    [InlineData("AFT JUN 1840")]
    [InlineData("BET 1840 AND 1845")]
    [InlineData("FROM 1840")]
    [InlineData("FROM 1840 TO 1845")]
    [InlineData("TO 1845")]
    [InlineData("44 BCE")]
    [InlineData("JULIAN 1 MAR 1700")]
    [InlineData("HEBREW 1 TSH 5600")]
    [InlineData("FRENCH_R 3 VEND 8")]
    public void AGedcom7DateValue_IsValid(string date) => Assert.True(GedDateSyntax.IsValid(date));

    [Theory]
    [InlineData("1840-06-01")]
    [InlineData("accessed 2026-07-06")]
    [InlineData("1 JUN 1840 Salem")]
    [InlineData("June 1840")]
    [InlineData("1 SEPT 1796")]
    [InlineData("31 JUN 1840")]
    [InlineData("1745/46")]
    [InlineData("BET 1840")]
    [InlineData("ABT")]
    [InlineData("1840  ")]
    [InlineData("1  JUN 1840")]
    [InlineData("HEBREW 1 JAN 5600")]
    [InlineData("")]
    public void AnythingElse_IsNot(string date) => Assert.False(GedDateSyntax.IsValid(date));

    [Fact]
    public void TheRefusalNamesTheFieldAndValue() =>
        Assert.StartsWith("facts[0].date \"1840-06-01\" is not a GEDCOM date", GedDateSyntax.Check("facts[0].date", "1840-06-01"));
}

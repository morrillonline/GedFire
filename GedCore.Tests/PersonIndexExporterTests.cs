using System.Text.Json;
using GedCore.Ged55;
using GedFire.Export;
using GedFire.Gen;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// GedDate.Qualifier — leading date-qualifier extraction
// ---------------------------------------------------------------------------

public class GedDateQualifierTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("15 JUN 1800", null)]
    [InlineData("1800", null)]
    [InlineData("ABT 1780", "ABT")]
    [InlineData("abt 1780", "ABT")]
    [InlineData("ABOUT 1780", "ABT")]
    [InlineData("EST 1780", "EST")]
    [InlineData("CAL 1780", "CAL")]
    [InlineData("BEF 1854", "BEF")]
    [InlineData("AFT 1854", "AFT")]
    [InlineData("BET 1850 AND 1860", "BET")]
    [InlineData("FROM 1850 TO 1860", "FROM")]
    public void Qualifier(string? input, string? expected)
        => Assert.Equal(expected, GedDate.Qualifier(input));
}

// ---------------------------------------------------------------------------
// PersonIndexExporter — synthetic-GEDCOM tests
// ---------------------------------------------------------------------------

public class PersonIndexExporterTests
{
    const string FamilyGed = """
        0 @I1@ INDI
        1 NAME Ezekiel /Hearth/
        1 SEX M
        1 BIRT
        2 DATE ABT 1780
        2 PLAC Canterbury, Merrimack, New Hampshire
        1 DEAT
        2 DATE 10 MAR 1854
        2 PLAC Concord, Merrimack, New Hampshire
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Sarah /Clough/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME John /Hearth/
        1 SEX M
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 MARR
        2 DATE 12 JAN 1805
        2 PLAC Canterbury, Merrimack, New Hampshire
        """;

    static JsonDocument Export(string gedText) =>
        JsonDocument.Parse(PersonIndexExporter.ToJson(
            ModelBuilder.Build(Ged55Parser.Parse(gedText)), "test.ged"));

    static JsonElement Person(JsonDocument doc, string xref) =>
        doc.RootElement.GetProperty("persons").EnumerateArray()
           .Single(p => p.GetProperty("xref").GetString() == xref);

    [Fact]
    public void Envelope_CountMatchesPersonsArray()
    {
        using var doc = Export(FamilyGed);
        var root = doc.RootElement;
        Assert.Equal("test.ged", root.GetProperty("source").GetString());
        Assert.Equal(3, root.GetProperty("count").GetInt32());
        Assert.Equal(3, root.GetProperty("persons").GetArrayLength());
    }

    [Fact]
    public void Father_CarriesFamilyWithSpouseChildrenAndMarriage()
    {
        using var doc = Export(FamilyGed);
        var fam = Person(doc, "@I1@").GetProperty("families").EnumerateArray().Single();

        Assert.Equal("@F1@", fam.GetProperty("xref").GetString());
        Assert.Equal("@I2@", fam.GetProperty("spouse").GetString());
        Assert.Equal(new[] { "@I3@" },
            fam.GetProperty("children").EnumerateArray().Select(c => c.GetString()).ToArray());

        var marr = fam.GetProperty("marriage");
        Assert.Equal(1805, marr.GetProperty("year").GetInt32());
        Assert.Equal("12 JAN 1805", marr.GetProperty("date").GetString());
        Assert.False(marr.TryGetProperty("qualifier", out _));
    }

    [Fact]
    public void Child_CarriesParentXrefs()
    {
        using var doc = Export(FamilyGed);
        var parents = Person(doc, "@I3@").GetProperty("parents");

        Assert.Equal("@F1@", parents.GetProperty("family").GetString());
        Assert.Equal("@I1@", parents.GetProperty("father").GetString());
        Assert.Equal("@I2@", parents.GetProperty("mother").GetString());
    }

    [Fact]
    public void ApproximateBirth_KeepsYearAndQualifier()
    {
        using var doc = Export(FamilyGed);
        var birth = Person(doc, "@I1@").GetProperty("birth");

        Assert.Equal("ABT 1780", birth.GetProperty("date").GetString());
        Assert.Equal(1780, birth.GetProperty("year").GetInt32());
        Assert.Equal("ABT", birth.GetProperty("qualifier").GetString());
        Assert.Equal("Canterbury, Merrimack, New Hampshire",
            birth.GetProperty("place").GetString());
    }

    [Fact]
    public void ExactDeath_HasNoQualifier()
    {
        using var doc = Export(FamilyGed);
        var death = Person(doc, "@I1@").GetProperty("death");

        Assert.Equal(1854, death.GetProperty("year").GetInt32());
        Assert.False(death.TryGetProperty("qualifier", out _));
    }

    [Fact]
    public void PersonWithoutEvents_OmitsBirthDeathAndParents()
    {
        using var doc = Export(FamilyGed);
        var wife = Person(doc, "@I2@");

        Assert.False(wife.TryGetProperty("birth", out _));
        Assert.False(wife.TryGetProperty("death", out _));
        Assert.False(wife.TryGetProperty("parents", out _));
    }

    [Fact]
    public void Names_AreNormalizedForMatching()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Mary Anne /O'Brien-Smith/
            1 SEX F
            """;
        using var doc = Export(ged);
        var p = Person(doc, "@I1@");

        Assert.Equal("MARY ANNE", p.GetProperty("given").GetString());
        Assert.Equal("OBRIEN SMITH", p.GetProperty("surname").GetString());
        Assert.Equal("Mary Anne O'Brien-Smith", p.GetProperty("name").GetString());
    }

    [Fact]
    public void UnknownSurname_KeepsPlaceholder()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Abigail /unknown/
            1 SEX F
            """;
        using var doc = Export(ged);
        Assert.Equal("____", Person(doc, "@I1@").GetProperty("surname").GetString());
    }

    [Fact]
    public void MultipleMarriages_ProduceOneFamilyEntryEach()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Benjamin /Hearth/
            1 SEX M
            1 FAMS @F1@
            1 FAMS @F2@
            0 @I2@ INDI
            1 NAME Elizabeth /Mitchell/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Abigail /unknown/
            1 SEX F
            1 FAMS @F2@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            0 @F2@ FAM
            1 HUSB @I1@
            1 WIFE @I3@
            """;
        using var doc = Export(ged);
        var spouses = Person(doc, "@I1@").GetProperty("families").EnumerateArray()
            .Select(f => f.GetProperty("spouse").GetString()).ToArray();

        Assert.Equal(new[] { "@I2@", "@I3@" }, spouses);
    }
}

using GedFire.Match;

namespace GedCore.Tests;

public class MatchIndexTests
{
    const string FamilyGed = """
        0 @I1@ INDI
        1 NAME Ezekiel /Hearth-Stone/
        1 SEX M
        1 BIRT
        2 DATE ABT 1780
        2 PLAC Canterbury, Merrimack, New Hampshire
        1 DEAT
        2 DATE 10 MAR 1854
        2 PLAC Concord, Merrimack, New Hampshire
        1 CENS
        2 DATE 1850
        2 PLAC Canterbury Township, Merrimack, New Hampshire
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Sarah /Clough/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Ezekiel /Hearth-Stone/
        1 SEX M
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME No Sex /Recorded/
        0 @I5@ INDI
        1 NAME No /Birth/
        1 SEX M
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 MARR
        2 DATE 12 JAN 1805
        """;

    static MatchIndex Index() => new(MatchTestModels.Build(FamilyGed));

    static PersonIndexEntry Entry(MatchIndex index, string xref) =>
        index.Entries.Single(e => e.Individual.Xref == xref);

    [Fact]
    public void NormalizesSurnameAndGiven_PreservingHyphens()
    {
        var entry = Entry(Index(), "@I1@");
        Assert.Equal("HEARTH-STONE", entry.NormalizedSurname);
        Assert.Equal("EZEKIEL", entry.NormalizedGiven);
    }

    [Fact]
    public void Birth_ParsesYearAndNormalizesPlace()
    {
        var entry = Entry(Index(), "@I1@");
        Assert.Equal(1780, entry.Birth!.Year);
        Assert.Equal("CANTERBURY MERRIMACK NEW HAMPSHIRE", entry.Birth.NormalizedPlace);
    }

    [Fact]
    public void Birth_NullWhenNoBirthEvent()
    {
        var entry = Entry(Index(), "@I5@");
        Assert.Null(entry.Birth);
    }

    [Fact]
    public void IsMale_TrueWhenSexRecordedMale()
        => Assert.Equal(true, Entry(Index(), "@I1@").IsMale);

    [Fact]
    public void IsMale_FalseWhenSexRecordedFemale()
        => Assert.Equal(false, Entry(Index(), "@I2@").IsMale);

    [Fact]
    public void IsMale_NullWhenSexNeverRecorded()
        => Assert.Null(Entry(Index(), "@I4@").IsMale);

    [Fact]
    public void Death_ParsesYearAndNormalizesPlace()
    {
        var entry = Entry(Index(), "@I1@");
        Assert.Equal(1854, entry.Death!.Year);
        Assert.Equal("CONCORD MERRIMACK NEW HAMPSHIRE", entry.Death.NormalizedPlace);
    }

    [Fact]
    public void Death_NullWhenNoneRecorded()
        => Assert.Null(Entry(Index(), "@I5@").Death);

    [Fact]
    public void Marriages_ResolveSpouseAndEventFromFamSpouse()
    {
        var husband = Entry(Index(), "@I1@");
        var husbandMarriage = Assert.Single(husband.Marriages);
        Assert.Equal("SARAH CLOUGH", husbandMarriage.NormalizedSpouseName);
        Assert.Equal(1805, husbandMarriage.Year);

        var wife = Entry(Index(), "@I2@");
        Assert.Equal("EZEKIEL HEARTH-STONE", Assert.Single(wife.Marriages).NormalizedSpouseName);
    }

    [Fact]
    public void Marriages_EmptyWhenNeverMarried()
        => Assert.Empty(Entry(Index(), "@I3@").Marriages);

    [Fact]
    public void Parents_ResolvedByRoleFromFamChild()
    {
        var child = Entry(Index(), "@I3@");
        Assert.Equal("EZEKIEL HEARTH-STONE", child.Parents!.NormalizedFatherName);
        Assert.Equal("SARAH CLOUGH", child.Parents.NormalizedMotherName);
    }

    [Fact]
    public void Parents_NullWhenNoFamChild()
        => Assert.Null(Entry(Index(), "@I1@").Parents);

    [Fact]
    public void Entries_CoverEveryIndividualInTheModel()
    {
        var model = MatchTestModels.Build(FamilyGed);
        var index = new MatchIndex(model);
        Assert.Equal(model.Individuals.Count, index.Entries.Count);
    }

    [Fact]
    public void Marriages_PreserveFamsOrderAndNormalizeMarriagePlaces()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Jane /Doe/
            1 FAMS @F2@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Alex /Smith/
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Beth /Jones/
            1 FAMS @F2@
            0 @F1@ FAM
            1 HUSB @I2@
            1 WIFE @I1@
            1 MARR
            2 DATE 1900
            2 PLAC Boston, Massachusetts
            0 @F2@ FAM
            1 HUSB @I1@
            1 WIFE @I3@
            1 MARR
            2 DATE 1920
            2 PLAC Portland, Maine
            """;

        var marriages = Entry(new MatchIndex(MatchTestModels.Build(ged)), "@I1@").Marriages;

        Assert.Equal(["BETH JONES", "ALEX SMITH"], marriages.Select(m => m.NormalizedSpouseName));
        Assert.Equal([1920, 1900], marriages.Select(m => m.Year));
        Assert.Equal(["PORTLAND MAINE", "BOSTON MASSACHUSETTS"], marriages.Select(m => m.NormalizedPlace));
    }
}

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
    public void BirthYear_ParsedFromApproximateDate()
    {
        var entry = Entry(Index(), "@I1@");
        Assert.Equal(1780, entry.BirthYear);
    }

    [Fact]
    public void BirthYear_NullWhenNoBirthEvent()
    {
        var entry = Entry(Index(), "@I5@");
        Assert.Null(entry.BirthYear);
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
    public void Places_CollectBirthDeathAndCensus_Normalized()
    {
        var entry = Entry(Index(), "@I1@");
        Assert.Equal(3, entry.NormalizedPlaces.Count);
        Assert.Contains("CANTERBURY MERRIMACK NEW HAMPSHIRE", entry.NormalizedPlaces);
        Assert.Contains("CONCORD MERRIMACK NEW HAMPSHIRE", entry.NormalizedPlaces);
        Assert.Contains("CANTERBURY TOWNSHIP MERRIMACK NEW HAMPSHIRE", entry.NormalizedPlaces);
    }

    [Fact]
    public void Places_EmptyWhenNoneRecorded()
        => Assert.Empty(Entry(Index(), "@I5@").NormalizedPlaces);

    [Fact]
    public void SpouseNames_ResolvedFromFamSpouse()
    {
        var husband = Entry(Index(), "@I1@");
        Assert.Equal(["SARAH CLOUGH"], husband.NormalizedSpouseNames);

        var wife = Entry(Index(), "@I2@");
        Assert.Equal(["EZEKIEL HEARTH-STONE"], wife.NormalizedSpouseNames);
    }

    [Fact]
    public void SpouseNames_EmptyWhenNeverMarried()
        => Assert.Empty(Entry(Index(), "@I3@").NormalizedSpouseNames);

    [Fact]
    public void ParentNames_ResolvedFromFamChild()
    {
        var child = Entry(Index(), "@I3@");
        Assert.Equal(2, child.NormalizedParentNames.Count);
        Assert.Contains("EZEKIEL HEARTH-STONE", child.NormalizedParentNames);
        Assert.Contains("SARAH CLOUGH", child.NormalizedParentNames);
    }

    [Fact]
    public void ParentNames_EmptyWhenNoFamChild()
        => Assert.Empty(Entry(Index(), "@I1@").NormalizedParentNames);

    [Fact]
    public void Entries_CoverEveryIndividualInTheModel()
    {
        var model = MatchTestModels.Build(FamilyGed);
        var index = new MatchIndex(model);
        Assert.Equal(model.Individuals.Count, index.Entries.Count);
    }
}

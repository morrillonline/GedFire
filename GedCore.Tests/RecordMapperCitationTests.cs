using GedFire.Mcp;

namespace GedCore.Tests;

// Citation detail, includeSources, and citedBy for RecordMapper. The record
// and family shapes themselves are covered in RecordMapperTests.
public class RecordMapperCitationTests
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @S1@ SOUR
        1 AUTH Parish Clerk
        1 TITL Parish Register
        1 PUBL Harwick Press
        0 @S2@ SOUR
        1 TITL Probate Volume 4
        0 @S3@ SOUR
        1 TITL Personal note
        1 NOTE Personal note
        2 CONT SHORTCITATION: PN|NOCITATION: TRUE|
        2 CONT .
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        2 SOUR @S3@
        1 BIRT
        2 DATE 1741
        2 SOUR @S1@
        3 PAGE p. 12
        3 QUAY 2
        3 DATA
        4 TEXT Cornelius, son of Levi
        1 DEAT
        2 DATE 1809
        2 SOUR @S1@
        3 PAGE p. 40
        2 SOUR @S1@
        3 PAGE p. 41
        2 SOUR @S999@
        1 BURI
        2 DATE 1809
        2 PLAC Harwick
        2 SOUR @S2@
        3 PAGE p. 7
        1 BAPM
        2 DATE 1741
        1 NOTE Prose about him.
        2 SOUR @S2@
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 MARR
        2 DATE 1770
        2 SOUR @S2@
        """;

    static readonly GedFire.Gen.GedModel Model = MatchTestModels.Build(Ged);
    static readonly RecordMapper Mapper = new("media");

    static PersonRecord Person(bool includeSources = false) =>
        Assert.IsType<PersonRecord>(Mapper.Map(Model, "@I1@", includeSources));

    [Fact]
    public void Citation_CarriesSourcePageQuotedTextAndQuality()
    {
        var citation = Assert.Single(Person().Birth!.Citations);

        Assert.Equal("citation", citation.Kind);
        Assert.Equal("@S1@", citation.Source);
        Assert.Equal("p. 12", citation.Page);
        Assert.Equal("Cornelius, son of Levi", citation.DataText);
        Assert.Equal(2, citation.Quay);
    }

    [Fact]
    public void Citation_WithoutPageTextOrQuality_HasNullFields()
    {
        var citation = Assert.Single(Person().Notes.Single().Citations);

        Assert.Equal("@S2@", citation.Source);
        Assert.Null(citation.Page);
        Assert.Null(citation.DataText);
        Assert.Null(citation.Quay);
    }

    [Fact]
    public void Citations_OfOneSourceOnOneFact_AreAllKeptAndDanglingOnesDropped()
    {
        var death = Person().Death!.Citations;

        Assert.Equal(["p. 40", "p. 41"], death.Select(c => c.Page));
        Assert.All(death, c => Assert.Equal("@S1@", c.Source));
    }

    [Fact]
    public void PersonalNoteSource_IsKindPersonalNote()
    {
        var citation = Assert.Single(Person().NameCitations);

        Assert.Equal("personalNote", citation.Kind);
        Assert.Equal("@S3@", citation.Source);
    }

    [Fact]
    public void IncludeSources_ListsEachCitedSourceOnceInFirstCitedOrder()
    {
        var sources = Person(includeSources: true).Sources!;

        Assert.Equal(["@S3@", "@S1@", "@S2@"], sources.Select(s => s.Xref));
        var parish = sources.Single(s => s.Xref == "@S1@");
        Assert.Equal("Parish Clerk", parish.Author);
        Assert.Equal("Parish Register", parish.Title);
        Assert.Equal("Harwick Press", parish.Publication);
    }

    [Fact]
    public void IncludeSources_IncludesSourcesCitedOnMarriages()
    {
        var family = Assert.IsType<FamilyRecord>(Mapper.Map(Model, "@F1@", includeSources: true));

        Assert.Equal(["@S2@"], family.Sources!.Select(s => s.Xref));
    }

    [Fact]
    public void IncludeSourcesOff_LeavesSourcesAbsent()
    {
        Assert.Null(Person().Sources);
        Assert.Null(Assert.IsType<FamilyRecord>(Mapper.Map(Model, "@F1@")).Sources);
    }

    [Fact]
    public void IncludeSources_DoesNotFollowRelatedRecordsCitations()
    {
        var spouse = Assert.IsType<PersonRecord>(Mapper.Map(Model, "@I2@", includeSources: true));

        // Only the marriage she shares; her husband's own birth, death, and name citations are not followed.
        Assert.Equal(["@S2@"], spouse.Sources!.Select(s => s.Xref));
    }

    [Fact]
    public void SourceLookup_ListsEveryStructureThatCitesItOncePerField()
    {
        var parish = Assert.IsType<SourceRecord>(Mapper.Map(Model, "@S1@"));
        var probate = Assert.IsType<SourceRecord>(Mapper.Map(Model, "@S2@"));

        Assert.Equal(
            [new CitedByEntry("@I1@", "person", "birth"), new CitedByEntry("@I1@", "person", "death")],
            parish.CitedBy);
        Assert.Equal(
            [new CitedByEntry("@I1@", "person", "otherEvent"), new CitedByEntry("@I1@", "person", "note"),
             new CitedByEntry("@F1@", "family", "marriage")],
            probate.CitedBy);
    }

    [Fact]
    public void SourceLookup_OfAnUncitedSource_HasEmptyCitedBy()
    {
        var model = MatchTestModels.Build("0 @S1@ SOUR\n1 TITL Lonely\n");

        var source = Assert.IsType<SourceRecord>(new RecordMapper("media").Map(model, "@S1@"));

        Assert.Empty(source.CitedBy!);
    }

    [Fact]
    public void SourcesCitedBy_AcrossRecords_ListsEachSourceOnce()
    {
        var records = new object[] { Mapper.Map(Model, "@I1@"), Mapper.Map(Model, "@F1@") };

        var sources = Mapper.SourcesCitedBy(Model, records);

        Assert.Equal(["@S3@", "@S1@", "@S2@"], sources.Select(s => s.Xref));
        Assert.All(sources, s => Assert.Null(s.CitedBy));
    }

    [Fact]
    public void OtherEvents_AreListedByTagInDocumentOrderWithTheirCitations()
    {
        var events = Person().OtherEvents;

        Assert.Equal(["BURI", "BAPM"], events.Select(e => e.Tag));
        var burial = events[0];
        Assert.Equal("1809", burial.Date);
        Assert.Equal("Harwick", burial.Place);
        var citation = Assert.Single(burial.Citations);
        Assert.Equal("@S2@", citation.Source);
        Assert.Equal("p. 7", citation.Page);
        Assert.Empty(events[1].Citations);
    }

    [Fact]
    public void OtherEvents_SourceIsIncludedAndCitedByNamesTheField()
    {
        Assert.Contains(Person(includeSources: true).Sources!, s => s.Xref == "@S2@");

        var source = Assert.IsType<SourceRecord>(Mapper.Map(Model, "@S2@"));
        Assert.Contains(source.CitedBy!, e => e.Xref == "@I1@" && e.Field == "otherEvent");
    }
}

using GedFire.Mcp;

namespace GedCore.Tests;

public class RecordMapperTests
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @S1@ SOUR
        1 TITL Parish Register
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        """;

    static readonly GedFire.Gen.GedModel Model = MatchTestModels.Build(Ged);
    static readonly RecordMapper Mapper = new("media");

    [Fact]
    public void Map_PersonXref_ReturnsPersonRecord()
    {
        var record = Assert.IsType<PersonRecord>(Mapper.Map(Model, "@I1@"));

        Assert.Equal("person", record.RecordType);
        Assert.Equal("Cornelius Ashworth", record.Name);
    }

    [Fact]
    public void Map_FamilyXref_ReturnsFamilyRecordWithSpouses()
    {
        var record = Assert.IsType<FamilyRecord>(Mapper.Map(Model, "@F1@"));

        Assert.Equal("@I1@", record.Husband!.Xref);
        Assert.Equal("@I2@", record.Wife!.Xref);
    }

    [Fact]
    public void Map_SourceXref_ReturnsSourceRecord()
    {
        var record = Assert.IsType<SourceRecord>(Mapper.Map(Model, "@S1@"));

        Assert.Equal("Parish Register", record.Title);
    }

    [Fact]
    public void Map_UnknownXref_ReturnsNotFound()
    {
        var record = Assert.IsType<NotFoundRecord>(Mapper.Map(Model, "@I99@"));

        Assert.Equal("not_found", record.RecordType);
        Assert.Equal("@I99@", record.Xref);
    }

    [Fact]
    public void Constructor_BlankMediaDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RecordMapper(""));
    }
}

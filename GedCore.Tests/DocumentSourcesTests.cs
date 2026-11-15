using System.Text;
using GedCore.Ged70;
using GedCore.Packages;

namespace GedCore.Tests;

public class DocumentSourcesTests
{
    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @S1@ SOUR
        1 TITL Parish Register
        1 NOTE Parish Register, online at https://records.example.org/register (accessed 1 Jan 2026)
        0 @S2@ SOUR
        1 TITL County Census
        0 @I1@ INDI
        1 NAME Ada /Vale/
        0 TRLR
        """;

    static readonly DocumentSources Sources = new(() => Ged70Parser.Read(new MemoryStream(Encoding.UTF8.GetBytes(Document))));

    [Fact]
    public void ASource_GivesItsTitleAndTheAddressItsNoteHolds()
    {
        Assert.Equal(new SourceDescription("Parish Register", "https://records.example.org/register"), Sources.Source("@S1@"));
        Assert.Equal(new SourceDescription("County Census", null), Sources.Source("@S2@"));
    }

    [Fact]
    public void ARecordThatIsNotASource_IsNone()
    {
        Assert.Null(Sources.Source("@I1@"));
        Assert.Null(Sources.Source("@S9@"));
    }
}

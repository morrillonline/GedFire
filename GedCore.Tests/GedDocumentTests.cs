using GedCore;
using GedCore.Ged55;
using GedCore.Ged70;

namespace GedCore.Tests;

// GedDocument.Version — HEAD.GEDC.VERS lookup shared by ConformanceChecker's
// GED011 check and GedFire's get_document_stats MCP tool.
public class GedDocumentTests
{
    [Fact]
    public void Version_ReadsDeclaredVersion_Gedcom70()
    {
        var doc = Ged70Parser.Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 TRLR

            """);
        Assert.Equal("7.0", doc.Version);
    }

    [Fact]
    public void Version_ReadsDeclaredVersion_Gedcom55()
    {
        var doc = Ged55Parser.Parse("""
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            2 FORM LINEAGE-LINKED
            1 CHAR ANSI
            0 TRLR

            """);
        Assert.Equal("5.5.1", doc.Version);
    }

    [Fact]
    public void Version_NullWhenGedcSubstructureAbsent()
    {
        var doc = Ged55Parser.Parse("""
            0 HEAD
            1 CHAR ANSI
            0 TRLR

            """);
        Assert.Null(doc.Version);
    }

    [Fact]
    public void Version_NullWhenVersSubstructureAbsent()
    {
        var doc = Ged55Parser.Parse("""
            0 HEAD
            1 GEDC
            2 FORM LINEAGE-LINKED
            0 TRLR

            """);
        Assert.Null(doc.Version);
    }

    [Fact]
    public void Version_NullWhenHeaderRecordAbsent()
    {
        var doc = Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME Solo /Person/
            0 TRLR

            """);
        Assert.Null(doc.Version);
    }
}

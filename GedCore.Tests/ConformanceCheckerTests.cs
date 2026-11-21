using GedCore.Ged70;
using GedCore.Validate;

namespace GedCore.Tests;

/// <summary>
/// One test per GED001-GED017 rule, each built from a minimal in-memory
/// document exhibiting exactly that
/// violation, plus two "clean document" tests proving the checker doesn't
/// false-positive on real, valid GEDCOM 7 content.
/// </summary>
public class ConformanceCheckerTests
{
    private static GedDocument Parse(string ged) => Ged70Parser.Parse(ged.Replace("\r\n", "\n"));

    private static GedDiagnostic Single(GedDocument doc, string code)
    {
        var diags = ConformanceChecker.Check(doc);
        var matches = diags.Where(d => d.Code == code).ToList();
        Assert.True(matches.Count >= 1, $"expected at least one {code} diagnostic, got: {string.Join(", ", diags.Select(d => d.Code))}");
        return matches[0];
    }

    [Fact]
    public void GED001_InvalidTagCharset_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 bad value
            0 TRLR
            """);

        var diag = Single(doc, "GED001");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("bad", diag.Tag);
    }

    [Fact]
    public void GED002_LevelSkipOrphansARecord_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Test /Person/
            3 FOO bar
            0 TRLR
            """);

        var diag = Single(doc, "GED002");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("FOO", diag.Tag);
    }

    [Fact]
    public void GED003_ContAfterSubstructure_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NOTE Hello
            2 DATE 1 JAN 2000
            2 CONT world
            0 TRLR
            """);

        var diag = Single(doc, "GED003");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("CONT", diag.Tag);
    }

    [Fact]
    public void GED004_DanglingPointer_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 FAMS @F999@
            0 TRLR
            """);

        var diag = Single(doc, "GED004");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("FAMS", diag.Tag);
    }

    [Fact]
    public void GED005_PointerWrongTargetType_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 FAMS @I2@
            0 @I2@ INDI
            1 NAME Wrong /Target/
            0 TRLR
            """);

        var diag = Single(doc, "GED005");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("FAMS", diag.Tag);
    }

    [Fact]
    public void GED006_SelfReferentialAlia_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 ALIA @I1@
            0 TRLR
            """);

        var diag = Single(doc, "GED006");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GED007_ExidWithoutType_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 EXID 12345
            0 TRLR
            """);

        var diag = Single(doc, "GED007");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GED008_DeprecatedAddressLine_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 RESI
            2 ADR1 123 Main St
            0 TRLR
            """);

        var diag = Single(doc, "GED008");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("ADR1", diag.Tag);
    }

    [Fact]
    public void GED009_DuplicateFamc_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 FAMC @F1@
            1 FAMC @F1@
            0 @F1@ FAM
            0 TRLR
            """);

        var diag = Single(doc, "GED009");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("FAMC", diag.Tag);
    }

    [Fact]
    public void GED009_DuplicateChil_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @F1@ FAM
            1 CHIL @I1@
            1 CHIL @I1@
            0 @I1@ INDI
            0 TRLR
            """);

        var diag = Single(doc, "GED009");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
        Assert.Equal("CHIL", diag.Tag);
    }

    [Fact]
    public void GED009_MultipleVoidChil_AreNotDuplicates()
    {
        // Several "1 CHIL @VOID@" lines legitimately record several
        // placeholder children — @VOID@ names no record, so no link repeats.
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @F1@ FAM
            1 CHIL @VOID@
            1 CHIL @VOID@
            0 TRLR
            """);

        Assert.DoesNotContain(ConformanceChecker.Check(doc), d => d.Code == "GED009");
    }

    [Fact]
    public void GED010_UndeclaredExtensionTag_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 _CUSTOM foo
            0 TRLR
            """);

        var diag = Single(doc, "GED010");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("_CUSTOM", diag.Tag);
    }

    [Fact]
    public void GED010_DeclaredExtensionTag_ProducesNoDiagnostic()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            1 SCHMA
            2 TAG _CUSTOM https://example.com/gedcom/CUSTOM
            0 @I1@ INDI
            1 _CUSTOM foo
            0 TRLR
            """);

        Assert.DoesNotContain(ConformanceChecker.Check(doc), d => d.Code == "GED010");
    }

    [Theory]
    [InlineData("""
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Somewhat Very Long Name That Needs
        2 CONC Continuing
        0 TRLR
        """, "CONC")]
    [InlineData("""
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 SOUR Some free text citation
        0 TRLR
        """, "SOUR")]
    [InlineData("""
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @N1@ NOTE Some text
        0 TRLR
        """, "NOTE")]
    [InlineData("""
        0 HEAD
        1 CHAR UTF-8
        1 GEDC
        2 VERS 7.0
        0 TRLR
        """, "CHAR")]
    [InlineData("""
        0 HEAD
        1 FILE somefile.ged
        1 GEDC
        2 VERS 7.0
        0 TRLR
        """, "FILE")]
    [InlineData("""
        0 HEAD
        1 DEST Somewhere
        1 GEDC
        2 VERS 7.0
        0 TRLR
        """, "DEST")]
    [InlineData("""
        0 HEAD
        1 GEDC
        2 VERS 7.0
        2 FORM LINEAGE-LINKED
        0 TRLR
        """, "FORM")]
    public void GED011_Removed70Structure_IsFlagged(string ged, string expectedTag)
    {
        var doc = Parse(ged);
        var diags = ConformanceChecker.Check(doc);
        var diag = Assert.Single(diags);
        Assert.Equal("GED011", diag.Code);
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal(expectedTag, diag.Tag);
    }

    [Fact]
    public void GED011_NotDiagnosedForA55Document()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @I1@ INDI
            1 NAME Somewhat Very Long Name That Needs
            2 CONC Continuing
            0 TRLR
            """);

        Assert.DoesNotContain(ConformanceChecker.Check(doc), d => d.Code == "GED011");
    }

    [Fact]
    public void GED011_HeadSourProductIdentifier_IsNotFlagged()
    {
        // HEAD.SOUR names the originating product (e.g. "1 SOUR FTM") — free
        // text by design, a wholly different structure from a citation SOUR
        // pointer. Regression test for a false positive found while running
        // the validate verb against a GEDCOM 7 document.
        var doc = Parse("""
            0 HEAD
            1 SOUR FTM
            1 GEDC
            2 VERS 7.0
            0 TRLR
            """);

        Assert.DoesNotContain(ConformanceChecker.Check(doc), d => d.Code == "GED011");
    }

    [Fact]
    public void GED012_SexOutsideRange_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 SEX Z
            0 TRLR
            """);

        var diag = Single(doc, "GED012");
        Assert.Equal(GedDiagnosticSeverity.Info, diag.Severity);
        Assert.Equal("SEX", diag.Tag);
    }

    [Fact]
    public void GED012_QuayOutsideRange_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 SOUR @S1@
            2 QUAY 9
            0 @S1@ SOUR
            1 TITL Test
            0 TRLR
            """);

        var diag = Single(doc, "GED012");
        Assert.Equal(GedDiagnosticSeverity.Info, diag.Severity);
        Assert.Equal("QUAY", diag.Tag);
    }

    [Fact]
    public void GED013_ObjeWithoutFile_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @O1@ OBJE
            1 TITL Some media
            0 TRLR
            """);

        var diag = Single(doc, "GED013");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("OBJE", diag.Tag);
    }

    [Fact]
    public void GED013_FileWithoutForm_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @O1@ OBJE
            1 FILE photo.jpg
            0 TRLR
            """);

        var diag = Single(doc, "GED013");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("FILE", diag.Tag);
    }

    [Fact]
    public void GED014_SourceObjeCycle_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @O1@ OBJE
            1 FILE photo.jpg
            2 FORM JPEG
            1 SOUR @S1@
            0 @S1@ SOUR
            1 TITL Test
            1 OBJE @O1@
            0 TRLR
            """);

        var diag = Single(doc, "GED014");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("@O1@", diag.Xref);
    }

    [Fact]
    public void GED015_FamilyEventOnPerson_IsFlaggedWithItsXref()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 MARR
            2 DATE 4 MAY 1901
            2 PLAC Elmford
            1 DIV
            0 @F1@ FAM
            1 HUSB @I1@
            1 MARR
            2 DATE 4 MAY 1901
            0 TRLR
            """);

        var diags = ConformanceChecker.Check(doc).Where(d => d.Code == "GED015").ToList();

        Assert.Equal(["MARR", "DIV"], diags.Select(d => d.Tag));
        Assert.All(diags, d => Assert.Equal("@I1@", d.Xref));
        Assert.All(diags, d => Assert.Equal(GedDiagnosticSeverity.Error, d.Severity));
        Assert.Contains("4 MAY 1901, Elmford", diags[0].Message);
    }

    [Fact]
    public void GED016_GarbledText_IsWarnedWithItsRepair_AndForeignTextIsNot()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME José /Núñez Peña/
            1 BIRT
            2 PLAC Łódź, Zażółć, Straße, Ærøskøbing, Αθήνα, Київ
            1 NOTE Born in Århus
            2 CONT at the CafÃ© MÃ¼ller
            0 @S1@ SOUR
            1 TITL Register
            0 TRLR
            """);

        var diag = Assert.Single(ConformanceChecker.Check(doc), d => d.Code == "GED016");

        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("NOTE", diag.Tag);
        Assert.Contains("\"Ã© MÃ¼\"", diag.Message);
        Assert.Contains("\"é Mü\"", diag.Message);
    }

    [Fact]
    public void GED017_DatesThatAreNotGedcom7_AreErrorsNamingThePayload()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE March 5, 1806
            1 DEAT
            2 DATE 9 Sep 1870
            1 BURI
            2 DATE
            1 CENS
            2 SDATE 1699/00
            0 TRLR
            """);

        var diags = ConformanceChecker.Check(doc).Where(d => d.Code == "GED017").ToList();

        Assert.All(diags, d => Assert.Equal((GedDiagnosticSeverity.Error, "@I1@"), (d.Severity, d.Xref)));
        Assert.Equal(
            [
                "DATE payload 'March 5, 1806' is not a GEDCOM 7 date",
                "DATE payload '9 Sep 1870' must be upper case: '9 SEP 1870'",
                "DATE has no payload and no PHRASE",
                "SDATE payload '1699/00' is not a GEDCOM 7 date",
            ],
            diags.Select(d => d.Message));
    }

    [Fact]
    public void GED017_ValidDatesAndAPhraseOnlyDate_AreNotFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            1 DATE 21 NOV 2026
            2 TIME 16:00:00Z
            0 @I1@ INDI
            1 BIRT
            2 DATE 05 MAR 1806
            1 CHR
            2 DATE ABT 1806
            1 RESI
            2 DATE FROM 1830 TO 1840
            1 CENS
            2 DATE BET 1 JUN 1850 AND 30 JUN 1850
            1 DEAT
            2 DATE
            3 PHRASE in the winter after the great storm
            1 BURI
            2 DATE JULIAN 12 FEB 1700
            0 TRLR
            """);

        Assert.DoesNotContain(ConformanceChecker.Check(doc), d => d.Code == "GED017");
    }

    [Fact]
    public void GED017_A55Document_IsNotHeldToGedcom7Dates()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @I1@ INDI
            1 BIRT
            2 DATE 12 FEB 1699/00
            0 TRLR
            """);

        Assert.DoesNotContain(ConformanceChecker.Check(doc), d => d.Code == "GED017");
    }

    // -------------------------------------------------------------------
    // Clean-document tests — no false positives on real valid content.
    // -------------------------------------------------------------------

    [Fact]
    public void CleanDocument_Seeded_ProducesNoDiagnostics()
    {
        var doc = Ged70DocumentFactory.CreateSeeded("Test Person");
        Assert.Empty(ConformanceChecker.Check(doc));
    }

    [Fact]
    public void CleanDocument_Example70Fixture_ProducesNoDiagnostics()
    {
        var doc = Ged70Parser.Read(new MemoryStream(ReadResource("Example-7.0.ged")));
        Assert.Empty(ConformanceChecker.Check(doc));
    }

    private static byte[] ReadResource(string fileName)
    {
        var stream = typeof(ConformanceCheckerTests).Assembly
            .GetManifestResourceStream($"GedCore.Tests.TestData.{fileName}")
            ?? throw new InvalidOperationException(
                $"Embedded resource GedCore.Tests.TestData.{fileName} not found.");
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }
}

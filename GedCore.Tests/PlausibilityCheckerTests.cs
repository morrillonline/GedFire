using GedCore.Ged70;
using GedCore.Validate;

namespace GedCore.Tests;

/// <summary>
/// One test per GEN1xx/GEN3xx rule, each built from a minimal in-memory
/// document exhibiting exactly that violation, plus a "clean document" test
/// proving the checker doesn't false-positive on an ordinary small family.
/// See docs/design/plausibility-checker.md.
/// </summary>
public class PlausibilityCheckerTests
{
    private static GedDocument Parse(string ged) => Ged70Parser.Parse(ged.Replace("\r\n", "\n"));

    private static GedDiagnostic Single(GedDocument doc, string code)
    {
        var diags = PlausibilityChecker.Check(doc);
        var matches = diags.Where(d => d.Code == code).ToList();
        Assert.True(matches.Count >= 1, $"expected at least one {code} diagnostic, got: {string.Join(", ", diags.Select(d => d.Code))}");
        return matches[0];
    }

    [Fact]
    public void GEN101_DeathBeforeBirth_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1980
            1 DEAT
            2 DATE 1970
            0 TRLR
            """);

        var diag = Single(doc, "GEN101");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("DEAT", diag.Tag);
    }

    [Fact]
    public void GEN101_MarriageAfterSpouseDeath_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1900
            1 DEAT
            2 DATE 1930
            0 @I2@ INDI
            1 BIRT
            2 DATE 1900
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE 1950
            0 TRLR
            """);

        var diag = Single(doc, "GEN101");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
        Assert.Contains("@I1@", diag.Message);
    }

    [Fact]
    public void GEN102_FatherTooOldAtChildBirth_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1900
            0 @I2@ INDI
            1 BIRT
            2 DATE 1990
            0 @F1@ FAM
            1 HUSB @I1@
            1 CHIL @I2@
            0 TRLR
            """);

        var diag = Single(doc, "GEN102");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
        Assert.Contains("father", diag.Message);
    }

    [Fact]
    public void GEN104_LargeSpousalAgeGap_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1900
            0 @I2@ INDI
            1 BIRT
            2 DATE 1950
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            0 TRLR
            """);

        var diag = Single(doc, "GEN104");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
    }

    [Fact]
    public void GEN105_LargeChildrenSpan_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1900
            0 @I2@ INDI
            1 BIRT
            2 DATE 1935
            0 @F1@ FAM
            1 CHIL @I1@
            1 CHIL @I2@
            0 TRLR
            """);

        var diag = Single(doc, "GEN105");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
    }

    [Fact]
    public void GEN103_MarriageUnderTwelve_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1990
            0 @I2@ INDI
            1 BIRT
            2 DATE 1970
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE 1995
            0 TRLR
            """);

        var diag = Single(doc, "GEN103");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
    }

    [Fact]
    public void GEN111_FamilyNchiZeroWithChildren_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            0 @F1@ FAM
            1 NCHI 0
            1 CHIL @I1@
            0 TRLR
            """);

        var diag = Single(doc, "GEN111");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
        Assert.Equal("NCHI", diag.Tag);
    }

    [Fact]
    public void GEN115_NoDeathRecorded_IsNotFlagged()
    {
        // An unresearched death is not itself a problem -- only a death or
        // burial record whose date, combined with BIRT, implies an
        // implausible age is worth flagging.
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1800
            0 TRLR
            """);

        Assert.Empty(PlausibilityChecker.Check(doc).Where(d => d.Code == "GEN115"));
    }

    [Fact]
    public void GEN115_ImplausibleAgeAtDeath_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1800
            1 DEAT
            2 DATE 1925
            0 TRLR
            """);

        var diag = Single(doc, "GEN115");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("DEAT", diag.Tag);
    }

    [Fact]
    public void GEN115_ImplausibleAgeAtBurial_NoDeathRecorded_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1800
            1 BURI
            2 DATE 1925
            0 TRLR
            """);

        var diag = Single(doc, "GEN115");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
        Assert.Equal("BURI", diag.Tag);
    }

    [Fact]
    public void GEN114_DiedYoungWithMarriageRecorded_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 BIRT
            2 DATE 1900
            1 DEAT
            2 DATE 1905
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 MARR
            0 TRLR
            """);

        var diag = Single(doc, "GEN114");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GEN301_MatchingNameAndBirth_IsFlaggedAsPossibleDuplicate()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME John /Smith/
            1 BIRT
            2 DATE 1900
            2 PLAC Boston, Massachusetts
            0 @I2@ INDI
            1 NAME John /Smith/
            1 BIRT
            2 DATE 1900
            2 PLAC Boston, Massachusetts
            0 TRLR
            """);

        var diag = Single(doc, "GEN301");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("@I1@", diag.Message);
        Assert.Contains("@I2@", diag.Message);
    }

    [Fact]
    public void GEN302_AncestorCycle_IsFlaggedAsError()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 FAMC @F1@
            0 @I2@ INDI
            1 FAMC @F2@
            0 @F1@ FAM
            1 HUSB @I2@
            0 @F2@ FAM
            1 HUSB @I1@
            0 TRLR
            """);

        var diag = Single(doc, "GEN302");
        Assert.Equal(GedDiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void GEN303_TooManyChildren_IsFlagged()
    {
        var lines = new List<string> {
            "0 HEAD", "1 GEDC", "2 VERS 7.0",
            "0 @M1@ INDI", "1 SEX F", "1 FAMS @F1@",
        };
        var famLines = new List<string> { "0 @F1@ FAM", "1 WIFE @M1@" };
        for (int i = 1; i <= 13; i++)
        {
            lines.Add($"0 @C{i}@ INDI");
            lines.Add($"1 FAMC @F1@");
            famLines.Add($"1 CHIL @C{i}@");
        }
        lines.AddRange(famLines);
        lines.Add("0 TRLR");

        var diag = Single(Parse(string.Join("\n", lines)), "GEN303");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@M1@", diag.Xref);
    }

    [Fact]
    public void GEN304_MultipleParentFamilies_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 FAMC @F1@
            1 FAMC @F2@
            0 @F1@ FAM
            0 @F2@ FAM
            0 TRLR
            """);

        var diag = Single(doc, "GEN304");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GEN305_ManySpouses_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 FAMS @F1@
            1 FAMS @F2@
            1 FAMS @F3@
            1 FAMS @F4@
            0 @I2@ INDI
            0 @I3@ INDI
            0 @I4@ INDI
            0 @I5@ INDI
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            0 @F2@ FAM
            1 HUSB @I1@
            1 WIFE @I3@
            0 @F3@ FAM
            1 HUSB @I1@
            1 WIFE @I4@
            0 @F4@ FAM
            1 HUSB @I1@
            1 WIFE @I5@
            0 TRLR
            """);

        var diag = Single(doc, "GEN305");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GEN306_DisconnectedIndividual_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Lonely /Person/
            0 TRLR
            """);

        var diag = Single(doc, "GEN306");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GEN401_EmojiInName_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME John \U0001F600 /Smith/
            0 TRLR
            """.Replace("\\U0001F600", "\U0001F600"));

        var diag = Single(doc, "GEN401");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GEN401_DigitInName_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME John3 /Smith/
            0 TRLR
            """);

        var diag = Single(doc, "GEN401");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("3", diag.Message);
    }

    [Fact]
    public void GEN401_OtherPunctuationInName_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME John_Q /Smith/
            0 TRLR
            """);

        Single(doc, "GEN401");
    }

    [Fact]
    public void GEN401_DiacriticsHyphenApostropheAndPeriodAreAllowed()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Renée H. O'Brien-Müller /Søren/ Jr.
            0 TRLR
            """);

        Assert.Empty(PlausibilityChecker.Check(doc).Where(d => d.Code == "GEN401"));
    }

    [Fact]
    public void GEN402_MissingSex_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME No /Sex/
            0 TRLR
            """);

        var diag = Single(doc, "GEN402");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@I1@", diag.Xref);
    }

    [Fact]
    public void GEN402_ExplicitUndeterminedSex_IsNotFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Undetermined /Sex/
            1 SEX U
            0 TRLR
            """);

        Assert.Empty(PlausibilityChecker.Check(doc).Where(d => d.Code == "GEN402"));
    }

    [Fact]
    public void GEN403_HusbandRecordedFemale_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 SEX F
            0 @F1@ FAM
            1 HUSB @I1@
            0 TRLR
            """);

        var diag = Single(doc, "GEN403");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
        Assert.Equal("HUSB", diag.Tag);
    }

    [Fact]
    public void GEN403_WifeRecordedMale_IsFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 SEX M
            0 @F1@ FAM
            1 WIFE @I1@
            0 TRLR
            """);

        var diag = Single(doc, "GEN403");
        Assert.Equal(GedDiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal("@F1@", diag.Xref);
        Assert.Equal("WIFE", diag.Tag);
    }

    [Fact]
    public void CleanOrdinaryFamily_IsNotFlagged()
    {
        var doc = Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Robert /Anderson/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1950
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Baker/
            1 SEX F
            1 BIRT
            2 DATE 3 MAR 1952
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Susan /Anderson/
            1 SEX F
            1 BIRT
            2 DATE 10 JUN 1975
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            1 MARR
            2 DATE 15 JUN 1974
            0 TRLR
            """);

        Assert.Empty(PlausibilityChecker.Check(doc));
    }
}

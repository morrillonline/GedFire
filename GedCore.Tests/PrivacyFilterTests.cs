using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Tests for the publication privacy filter: plausibly-living individuals
/// (no death-class fact, born fewer than 100 years before generation) are
/// reduced to a "Living &lt;Surname&gt;" placeholder in the generated HTML;
/// known-dead and 100-plus-year-old individuals publish in full.
/// </summary>
public class PrivacyFilterTests
{
    const int GenerationYear = 2026;

    const string Template =
        "<html><head><title><insert title></title></head>" +
        "<body><insert body></body></html>";

    static GedModel BuildFiltered(string gedText)
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(gedText));
        PrivacyFilter.Apply(model, GenerationYear);
        return model;
    }

    static Dictionary<string, string> GenerateFiltered(string gedText)
    {
        var model = BuildFiltered(gedText);
        string dir = Path.Combine(Path.GetTempPath(), "gedfire-priv-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            return Directory.GetFiles(dir, "*.html")
                .ToDictionary(p => Path.GetFileName(p)!, p => File.ReadAllText(p));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // A dead couple (published in full) with three children:
    //   @I3@ born 1930, no death  -> plausibly living, privatized
    //   @I4@ born 1920, no death  -> 100+ years old, published
    //   @I5@ born 1930, has DEAT  -> known dead, published
    const string FamilyGed = """
        0 @I1@ INDI
        1 NAME John /Hearth/
        1 SEX M
        1 BIRT
        2 DATE 1 JAN 1895
        1 DEAT
        2 DATE 2 FEB 1960
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Smith/
        1 SEX F
        1 BIRT
        2 DATE 3 MAR 1898
        1 DEAT
        2 DATE 4 APR 1970
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Barbara Lois /Hearth/
        1 SEX F
        1 BIRT
        2 DATE 24 SEP 1930
        1 NOTE Private biographical detail.
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME Edith /Hearth/
        1 SEX F
        1 BIRT
        2 DATE 5 MAY 1920
        1 FAMC @F1@
        0 @I5@ INDI
        1 NAME Ralph /Hearth/
        1 SEX M
        1 BIRT
        2 DATE 6 JUN 1930
        1 DEAT
        2 DATE 7 JUL 1990
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 MARR
        2 DATE 8 AUG 1918
        1 CHIL @I3@
        1 CHIL @I4@
        1 CHIL @I5@
        """;

    [Fact]
    public void PlausiblyLiving_IsPrivatizedInModel()
    {
        var model = BuildFiltered(FamilyGed);
        var barbara = model.Individuals["@I3@"];

        Assert.Equal("Living", barbara.FirstName);
        Assert.Equal("Living Hearth", barbara.Fullname);
        Assert.Null(barbara.Birth);
        Assert.Empty(barbara.NameSources);
        Assert.Empty(barbara.NarrativeNotes);
    }

    [Fact]
    public void DeadAndCentenarian_AreNotPrivatized()
    {
        var model = BuildFiltered(FamilyGed);

        Assert.Equal("Edith", model.Individuals["@I4@"].FirstName);   // born 1920: 100+ years
        Assert.Equal("Ralph", model.Individuals["@I5@"].FirstName);   // has DEAT
        Assert.Equal("John",  model.Individuals["@I1@"].FirstName);   // has DEAT
    }

    [Fact]
    public void WillOrProbate_CountsAsDead()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Silas /Hearth/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1950
            1 WILL
            2 DATE 2 FEB 2000
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Silas", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void UndatedPerson_IsTreatedAsHistoric()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Abigail /Hearth/
            1 SEX F
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Abigail", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void GeneratedHtml_ShowsPlaceholderAndNoBirthData()
    {
        var files = GenerateFiltered(FamilyGed);
        string all = string.Join("\n", files.Values);

        Assert.Contains("Living Hearth", all);
        Assert.DoesNotContain("Barbara", all);
        Assert.DoesNotContain("1930</span>", files.Single(f => !f.Key.StartsWith("index")).Value
            .Split("Living Hearth")[1].Split("</div>")[0]);   // no year span on the living child card
        // The published siblings keep their data
        Assert.Contains("Edith Hearth", all);
        Assert.Contains("Ralph Hearth", all);
    }

    [Fact]
    public void MarriageOfLivingSpouse_IsSuppressed()
    {
        // Dead husband, living wife: the marriage date must not render.
        string ged = """
            0 @I1@ INDI
            1 NAME John /Hearth/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1900
            1 DEAT
            2 DATE 2 FEB 1980
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Doris /Gray/
            1 SEX F
            1 BIRT
            2 DATE 3 MAR 1935
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Frank /Hearth/
            1 SEX M
            1 BIRT
            2 DATE 4 APR 1875
            1 DEAT
            2 DATE 5 MAY 1950
            1 FAMS @F2@
            0 @F2@ FAM
            1 HUSB @I3@
            1 CHIL @I1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE 6 JUN 1955
            """;
        var model = BuildFiltered(ged);

        Assert.Null(model.Families["@F1@"].Marriage);
        Assert.Equal("Living", model.Individuals["@I2@"].FirstName);

        // And the HTML never mentions the marriage date or her name.
        var files = GenerateFiltered(ged);
        string all = string.Join("\n", files.Values);
        Assert.DoesNotContain("1955", all);
        Assert.DoesNotContain("Doris", all);
        Assert.Contains("Living Gray", all);
    }

    [Fact]
    public void IndexRow_UsesPlaceholderWithoutDates()
    {
        var files = GenerateFiltered(FamilyGed);
        string index = files["index0.html"];
        int at = index.IndexOf("Living Hearth", StringComparison.Ordinal);
        Assert.True(at >= 0);
        string row = index[at..index.IndexOf("</tr>", at, StringComparison.Ordinal)];
        Assert.DoesNotContain("1930", row);
    }

    // -------------------------------------------------------------------
    // RESN (restriction notice) — Subproject G
    // -------------------------------------------------------------------

    [Fact]
    public void ExplicitConfidential_BeatsDeadHeuristic()
    {
        // Born 1700 with a death fact: the heuristic alone says publishable.
        string ged = """
            0 @I1@ INDI
            1 NAME Constance /Hearth/
            1 SEX F
            1 BIRT
            2 DATE 1 JAN 1700
            1 DEAT
            2 DATE 2 FEB 1770
            1 RESN CONFIDENTIAL
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Living", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void ExplicitPrivacy_BeatsDeadHeuristic()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Constance /Hearth/
            1 SEX F
            1 BIRT
            2 DATE 1 JAN 1700
            1 DEAT
            2 DATE 2 FEB 1770
            1 RESN PRIVACY
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Living", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void ExplicitLocked_DoesNotPrivatize()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Constance /Hearth/
            1 SEX F
            1 BIRT
            2 DATE 1 JAN 1700
            1 DEAT
            2 DATE 2 FEB 1770
            1 RESN LOCKED
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Constance", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void ListPayload_ConfidentialAndLocked_Privatizes()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Constance /Hearth/
            1 SEX F
            1 BIRT
            2 DATE 1 JAN 1700
            1 DEAT
            2 DATE 2 FEB 1770
            1 RESN CONFIDENTIAL, LOCKED
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Living", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void LowercasePayload_IsNormalized()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Constance /Hearth/
            1 SEX F
            1 BIRT
            2 DATE 1 JAN 1700
            1 DEAT
            2 DATE 2 FEB 1770
            1 RESN confidential
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Living", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void UndatedPersonWithPrivacyResn_IsPrivatized()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME Abigail /Hearth/
            1 SEX F
            1 RESN PRIVACY
            """;
        var model = BuildFiltered(ged);
        Assert.Equal("Living", model.Individuals["@I1@"].FirstName);
    }

    [Fact]
    public void RestrictedSpouse_SuppressesMarriageLine()
    {
        // Dead husband (no RESN), living-by-restriction wife: marriage must not render.
        string ged = """
            0 @I1@ INDI
            1 NAME John /Hearth/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1900
            1 DEAT
            2 DATE 2 FEB 1980
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Doris /Gray/
            1 SEX F
            1 BIRT
            2 DATE 3 MAR 1850
            1 DEAT
            2 DATE 4 APR 1930
            1 RESN CONFIDENTIAL
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE 6 JUN 1955
            """;
        var model = BuildFiltered(ged);

        Assert.Null(model.Families["@F1@"].Marriage);
        Assert.Equal("Living", model.Individuals["@I2@"].FirstName);
    }
}

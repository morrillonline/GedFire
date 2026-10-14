using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>A burial with no death stands in for the death on generated pages; beside a death it is shown as a burial.</summary>
public class SiteGeneratorBurialTests
{
    const string Template =
        "<html><head><title><insert title></title></head>" +
        "<body><insert body></body></html>";

    // John has only a burial; Mary has both; Tom (childless) has only a burial; Ann has both.
    // Sue's husband Bob (childless, so shown inline) has only a burial.
    const string Ged = """
        0 @I1@ INDI
        1 NAME John /Smith/
        1 SEX M
        1 BIRT
        2 DATE 1 JAN 1810
        1 BURI
        2 DATE 8 FEB 1880
        2 PLAC Salem, Massachusetts
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Jones/
        1 SEX F
        1 BIRT
        2 DATE 2 FEB 1815
        1 DEAT
        2 DATE 5 MAR 1890
        1 BURI
        2 DATE 7 MAR 1890
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Tom /Smith/
        1 SEX M
        1 BIRT
        2 DATE 3 MAR 1840
        1 BURI
        2 DATE 9 APR 1911
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME Ann /Smith/
        1 SEX F
        1 BIRT
        2 DATE 4 APR 1842
        1 DEAT
        2 DATE 1 MAY 1930
        1 BURI
        2 DATE 3 MAY 1930
        1 FAMC @F1@
        0 @I5@ INDI
        1 NAME Sue /Smith/
        1 SEX F
        1 BIRT
        2 DATE 5 MAY 1845
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I6@ INDI
        1 NAME Bob /Brown/
        1 SEX M
        1 BIRT
        2 DATE 6 JUN 1840
        1 BURI
        2 DATE 12 DEC 1900
        1 FAMS @F2@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 CHIL @I4@
        1 CHIL @I5@
        0 @F2@ FAM
        1 HUSB @I6@
        1 WIFE @I5@
        """;

    static Dictionary<string, string> Generate(string gedText)
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(gedText));
        string dir = Path.Combine(Path.GetTempPath(), "gedfire-burial-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            return Directory.GetFiles(dir, "*.html")
                .ToDictionary(p => Path.GetFileName(p)!, p => File.ReadAllText(p));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static string FamilyPage(Dictionary<string, string> files) =>
        files.Single(f => !f.Key.StartsWith("index") && f.Value.Contains("class=\"role\">Husband<")).Value;

    [Fact]
    public void AParentWithOnlyABurial_ShowsItAsTheDeath()
    {
        string page = FamilyPage(Generate(Ged));

        Assert.Contains("<dt>Died</dt><dd>8 FEB 1880", page);
        Assert.DoesNotContain("Buried</dt><dd>8 FEB 1880", page);
    }

    [Fact]
    public void AParentWithBothADeathAndABurial_ShowsBoth()
    {
        string page = FamilyPage(Generate(Ged));

        Assert.Contains("<dt>Died</dt><dd>5 MAR 1890", page);
        Assert.Contains("<dt>Buried</dt><dd>7 MAR 1890", page);
    }

    [Fact]
    public void AChildWithOnlyABurial_ShowsItAsTheDeathAndInTheLifeSpan()
    {
        string page = FamilyPage(Generate(Ged));

        Assert.Contains("<dt>Died</dt><dd>9 APR 1911", page);
        Assert.Contains("<span class=\"yr\">1840 – 1911</span>", page);
    }

    [Fact]
    public void AChildWithBothADeathAndABurial_ShowsBothAndTheDeathYear()
    {
        string page = FamilyPage(Generate(Ged));

        Assert.Contains("<dt>Died</dt><dd>1 MAY 1930", page);
        Assert.Contains("<dt>Buried</dt><dd>3 MAY 1930", page);
        Assert.Contains("<span class=\"yr\">1842 – 1930</span>", page);
    }

    [Fact]
    public void ASpouseWithOnlyABurial_IsShownWithAD()
    {
        string page = FamilyPage(Generate(Ged));

        Assert.Contains("Bob b. 6 June 1840; d. 12 December 1900.", page);
    }

    [Fact]
    public void ASpouseWithADeathAndABurial_IsShownWithBoth()
    {
        string both = Ged.Replace("\r\n", "\n")
            .Replace("1 BURI\n2 DATE 12 DEC 1900", "1 DEAT\n2 DATE 10 DEC 1900\n1 BURI\n2 DATE 12 DEC 1900");

        string page = FamilyPage(Generate(both));

        Assert.Contains("d. 10 December 1900; bur. 12 December 1900.", page);
    }

    [Fact]
    public void ThePageName_UsesTheBurialYearWhenThereIsNoDeath()
    {
        Assert.Contains("SmithJohn-1810-1880.html", Generate(Ged).Keys);
    }

    [Fact]
    public void TheIndexOfNames_UsesTheBurialYearWhenThereIsNoDeath()
    {
        string index = Generate(Ged)["index0.html"];

        Assert.Contains("data-label=\"Died\">1880</td>", index);
        Assert.Contains("data-label=\"Died\">1930</td>", index);
        Assert.Contains("data-label=\"Place(s)\" class=\"sub\">Massachusetts", index);
    }
}

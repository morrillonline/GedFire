using System.Text.RegularExpressions;
using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// URL-resolution characterization tests (safety net for extracting the URL
/// logic out of SiteGenerator) plus the index linking rule and attribute
/// escaping.
/// </summary>
public class SiteGeneratorUrlAndIndexTests
{
    const string Template =
        "<html><head><title><insert title></title></head>" +
        "<body><insert body></body></html>";

    static GedModel BuildModel(string gedText) =>
        ModelBuilder.Build(Ged55Parser.Parse(gedText));

    static Dictionary<string, string> Generate(GedModel model)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gedfire-url-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            return Directory.GetFiles(dir, "*.html")
                .ToDictionary(p => Path.GetFileName(p)!, p => File.ReadAllText(p));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // A three-generation GED: Henry+Alice and Henry+Betty (two child-producing
    // marriages for Henry), plus a childless daughter Clara.
    const string MultiMarriageGed = """
        0 @I1@ INDI
        1 NAME Henry /Brown/
        1 SEX M
        1 BIRT
        2 DATE 1 JAN 1800
        1 DEAT
        2 DATE 1 JAN 1875
        1 FAMS @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME Alice /Green/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Betty /White/
        1 SEX F
        1 FAMS @F2@
        0 @I4@ INDI
        1 NAME ChildA /Brown/
        1 SEX M
        1 FAMC @F1@
        0 @I5@ INDI
        1 NAME Clara /Brown/
        1 SEX F
        1 FAMC @F2@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I4@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I3@
        1 CHIL @I5@
        """;

    // -----------------------------------------------------------------------
    // URL resolution characterization
    // -----------------------------------------------------------------------

    [Fact]
    public void IndividualUrl_ChildWithoutOwnFamily_IsParentsPage()
    {
        var model = BuildModel(MultiMarriageGed);
        var gen   = new SiteGenerator(model, Template);

        var clara = model.Individuals["@I5@"];
        var famF2 = model.Families["@F2@"];
        Assert.Equal(gen.GetFamilyUrl(famF2), gen.GetIndividualUrl(clara));
    }

    [Fact]
    public void IndividualUrl_PersonWithChildProducingMarriage_IsThatFamilyPage()
    {
        var model = BuildModel(MultiMarriageGed);
        var gen   = new SiteGenerator(model, Template);

        var henry = model.Individuals["@I1@"];
        var famF1 = model.Families["@F1@"];
        Assert.Equal(gen.GetFamilyUrl(famF1), gen.GetIndividualUrl(henry));
    }

    [Fact]
    public void FamilyUrl_MultiMarriageHusband_EncodesWifeInSecondUrl()
    {
        var model = BuildModel(MultiMarriageGed);
        var gen   = new SiteGenerator(model, Template);

        string url1 = gen.GetFamilyUrl(model.Families["@F1@"]);
        string url2 = gen.GetFamilyUrl(model.Families["@F2@"]);

        Assert.NotEqual(url1, url2);
        Assert.StartsWith("BrownHenry-1800-1875", url1);
        Assert.Contains("GreenAlice", url1);
        Assert.Contains("WhiteBetty", url2);
    }

    [Fact]
    public void FamilyUrl_IsStableAcrossRepeatedCalls()
    {
        var model = BuildModel(MultiMarriageGed);
        var gen   = new SiteGenerator(model, Template);

        var fam = model.Families["@F1@"];
        Assert.Equal(gen.GetFamilyUrl(fam), gen.GetFamilyUrl(fam));
    }

    // -----------------------------------------------------------------------
    // Index linking rule (CLAUDE.md): several child-producing marriages ⇒
    // each spouse in the Spouse(s) column links to that marriage's page.
    // -----------------------------------------------------------------------

    [Fact]
    public void IndexRow_MultiMarriagePerson_SpousesLinkToTheirFamilyPages()
    {
        var model = BuildModel(MultiMarriageGed);
        var gen   = new SiteGenerator(model, Template);
        string urlF1 = gen.GetFamilyUrl(model.Families["@F1@"]);
        string urlF2 = gen.GetFamilyUrl(model.Families["@F2@"]);

        var files = Generate(BuildModel(MultiMarriageGed));
        string index = files["index0.html"];

        // Henry's name is a plain span (no single page to link to)…
        Assert.Contains("<span class=\"nm\">Henry Brown</span>", index);
        // …and each spouse links to that marriage's family page.
        Assert.Contains($"<a href=\"{urlF1}\">Alice</a>", index);
        Assert.Contains($"<a href=\"{urlF2}\">Betty</a>", index);
    }

    [Fact]
    public void IndexRow_SingleMarriagePerson_SpouseColumnStaysPlainText()
    {
        var files = Generate(BuildModel(MultiMarriageGed));
        string index = files["index0.html"];

        // Alice has one family page — her name links, her spouse cell is text.
        Assert.Contains("\">Alice Green</a>", index);
        Assert.DoesNotContain(">Henry</a></td>", index);
    }

    // -----------------------------------------------------------------------
    // Dangling links: the index must never link to a page the generator
    // does not write (families with no husband, or minted URLs of childless
    // couples with no parents).
    // -----------------------------------------------------------------------

    [Fact]
    public void IndexRow_FamilyWithoutHusband_ShowsUnlinkedNamesInsteadOfDanglingLinks()
    {
        // F1 has children but no HUSB, so GenerateFamilyPages skips it — yet
        // GetIndividualUrl mints a URL for Wilma and her child. The index must
        // render their names unlinked rather than link a 404.
        string ged = """
            0 @I1@ INDI
            1 NAME Wilma /Stone/
            1 SEX F
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Kid /Stone/
            1 SEX M
            1 FAMC @F1@
            0 @F1@ FAM
            1 WIFE @I1@
            1 CHIL @I2@
            """;

        var files = Generate(BuildModel(ged));
        string index = files["index0.html"];

        // Both people still appear in the index…
        Assert.Contains("Wilma Stone", index);
        Assert.Contains("Kid Stone", index);

        // …and no href on any page points at a file that was not generated.
        AssertNoDanglingLinks(files);
    }

    [Fact]
    public void IndexRow_ChildlessCoupleWithNoParents_HasNoDanglingLink()
    {
        // GetFamilyUrl mints "StoneJohn-x-x.html" for the childless couple,
        // but no page is written — the wife's index row used to link to it.
        string ged = """
            0 @I1@ INDI
            1 NAME John /Stone/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Freda /Glass/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            """;

        var files = Generate(BuildModel(ged));

        Assert.Contains("Freda", files["index0.html"]);
        AssertNoDanglingLinks(files);
    }

    static void AssertNoDanglingLinks(Dictionary<string, string> files)
    {
        var hrefs = new Regex("href=\"([^\"#][^\"]*)\"");
        foreach (var (name, html) in files)
            foreach (Match m in hrefs.Matches(html))
            {
                string target = m.Groups[1].Value;
                if (target.StartsWith("http") || target.StartsWith("../")) continue;
                Assert.True(files.ContainsKey(target),
                    $"{name} links to '{target}', which was not generated");
            }
    }

    // -----------------------------------------------------------------------
    // Chained footnotes: two citations on one fact share a footnote; the
    // second is appended with "See also" — which needs a separating space.
    // -----------------------------------------------------------------------

    [Fact]
    public void ChainedFootnotes_SeparateCitationsWithSpace()
    {
        string ged = """
            0 @S1@ SOUR
            1 AUTH First Author
            1 TITL First Book
            0 @S2@ SOUR
            1 AUTH Second Author
            1 TITL Second Book
            0 @I1@ INDI
            1 NAME Abel /Chained/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1800
            2 SOUR @S1@
            2 SOUR @S2@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Chained/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Child /Chained/
            1 SEX M
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            """;

        var files = Generate(BuildModel(ged));
        string famPage = files.Single(f => !f.Key.StartsWith("index")).Value;

        Assert.Contains(". See also Second Author", famPage);
        Assert.DoesNotContain(".See also", famPage);
    }

    // -----------------------------------------------------------------------
    // Attribute escaping: URL values in href must never contain a raw '&'.
    // -----------------------------------------------------------------------

    [Fact]
    public void Hrefs_AreAttributeEscaped()
    {
        // "A&B" survives MakePageName (only listed characters are stripped),
        // so Thomas's page URL contains '&' — every href pointing at it must
        // escape it as &amp;.
        string ged = """
            0 @I1@ INDI
            1 NAME Abraham /Smith/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Sarah /Smith/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Thomas A&B /Smith/
            1 SEX M
            1 FAMC @F1@
            1 FAMS @F2@
            0 @I4@ INDI
            1 NAME Mary /Jones/
            1 SEX F
            1 FAMS @F2@
            0 @I5@ INDI
            1 NAME Robert /Smith/
            1 SEX M
            1 FAMC @F2@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            0 @F2@ FAM
            1 HUSB @I3@
            1 WIFE @I4@
            1 CHIL @I5@
            """;

        var files = Generate(BuildModel(ged));

        var rawAmpInHref = new Regex("href=\"[^\"]*&(?!amp;)");
        foreach (var (name, html) in files)
            Assert.False(rawAmpInHref.IsMatch(html),
                $"{name} contains an unescaped '&' inside an href attribute");

        // Sanity: the escaped form actually occurs somewhere.
        Assert.Contains(files.Values, h => h.Contains("A&amp;B"));
    }
}

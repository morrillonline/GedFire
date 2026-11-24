using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

// One fact citing the same source on two pages must render each page, once.
public class SiteGeneratorCitationTests : IDisposable
{
    readonly string _outDir = Directory.CreateTempSubdirectory("gedfire-gen-citation-tests-").FullName;

    const string Template =
        "<html><head><title><insert title></title></head><body><insert body></body></html>";

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @S1@ SOUR
        1 AUTH Parish Clerk
        1 TITL Parish Register
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1741
        2 SOUR @S1@
        3 PAGE p. 40
        2 SOUR @S1@
        3 PAGE p. 41
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 MARR
        2 DATE 1770
        1 CHIL @I3@
        0 @I3@ INDI
        1 NAME Levi /Ashworth/
        1 SEX M
        1 FAMC @F1@
        """;

    public void Dispose() => Directory.Delete(_outDir, recursive: true);

    string GenerateAllHtml()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(Ged));
        new SiteGenerator(model, Template).Generate(_outDir);
        return string.Join("\n", Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
    }

    static int Count(string text, string value) =>
        (text.Length - text.Replace(value, "").Length) / value.Length;

    [Fact]
    public void TwoPagesOfOneSourceOnOneFact_EachPageAppearsOnceInTheGeneratedSite()
    {
        string html = GenerateAllHtml();

        // The generator drops a leading "p. " from a page, so p. 40 reads ", 40."
        Assert.True(Count(html, ", 40.") > 0, "page 40 is not rendered");
        Assert.Equal(Count(html, ", 40."), Count(html, ", 41."));
    }

    const string CommentaryGed = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @S1@ SOUR
        1 AUTH Parish Clerk
        1 TITL Parish Register
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1741
        2 SOUR @S1@
        3 PAGE p. 40
        3 NOTE The clerk wrote the year only.
        1 DEAT
        2 DATE 1809
        2 SOUR @VOID@
        3 NOTE His sons disagreed about the month.
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 SOUR @S1@
        2 PAGE p. 52
        2 NOTE The banns name him Watkin.
        0 @I3@ INDI
        1 NAME Levi /Ashworth/
        1 SEX M
        1 FAMC @F1@
        0 TRLR
        """;

    string GenerateCommentaryHtml()
    {
        var model = ModelBuilder.Build(GedCore.Ged70.Ged70Parser.Parse(CommentaryGed));
        new SiteGenerator(model, Template).Generate(_outDir);
        return string.Join("\n", Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
    }

    [Fact]
    public void AnImportedNoteThatAlreadyNamesTheAuthorAnotherWay_IsNotGivenTheAuthorAgain()
    {
        string ged = CommentaryGed.Replace("""
            1 AUTH Parish Clerk
            1 TITL Parish Register
            """, """
            1 AUTH Clerk, Parish
            1 TITL Parish Register
            1 NOTE The Parish Clerk, Parish Register (Salem, 1801).
            """);
        var model = ModelBuilder.Build(GedCore.Ged70.Ged70Parser.Parse(ged));
        new SiteGenerator(model, Template).Generate(_outDir);
        string html = string.Join("\n", Directory.EnumerateFiles(_outDir, "*.html").Select(File.ReadAllText));

        Assert.Contains(">The Parish Clerk, Parish Register (Salem, 1801), 40.", html);
        Assert.DoesNotContain("Clerk, Parish,", html);
    }

    [Fact]
    public void CitationCommentary_FollowsTheSourceInItsFootnote()
    {
        string html = GenerateCommentaryHtml();

        Assert.Contains(", 40. The clerk wrote the year only.", html);
    }

    [Fact]
    public void CommentaryCitingNoSource_IsAFootnoteOfItsOwn()
    {
        string html = GenerateCommentaryHtml();

        Assert.True(Count(html, "His sons disagreed about the month.") > 0, "the commentary is not rendered");
        Assert.DoesNotContain("@VOID@", html);
    }

    [Fact]
    public void AFamilysOwnCitation_IsAFootnoteOnItsPage_WithItsCommentary()
    {
        string html = GenerateCommentaryHtml();

        Assert.Contains(", 52. The banns name him Watkin.", html);
    }

    // Nothing a source or citation says is markup: it is shown as written, never run.
    const string MarkupGed = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @S1@ SOUR
        1 AUTH Clerk <img src=x onerror=alert(1)>
        1 TITL Register of <illegible> & Sons
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        2 NOTE Known as <script>alert(3)</script> the elder
        1 SEX M
        1 BIRT
        2 DATE 1741
        2 SOUR @S1@
        3 PAGE p. <40>
        3 DATA
        4 TEXT born <script>alert(2)</script> at home
        3 NOTE The clerk wrote <b>the year</b> only.
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        0 @I3@ INDI
        1 NAME Levi /Ashworth/
        1 SEX M
        1 FAMC @F1@
        0 TRLR
        """;

    string GenerateMarkupHtml()
    {
        var model = ModelBuilder.Build(GedCore.Ged70.Ged70Parser.Parse(MarkupGed));
        new SiteGenerator(model, Template).Generate(_outDir);
        return string.Join("\n", Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
    }

    [Fact]
    public void MarkupInAFootnote_IsShownAsTextInEveryPartOfTheCitation()
    {
        string html = GenerateMarkupHtml();

        Assert.Contains("Clerk &lt;img src=x onerror=alert(1)&gt;", html);
        Assert.Contains("Register of &lt;illegible&gt; &amp; Sons", html);
        Assert.Contains("&lt;40&gt;", html);
        Assert.Contains("born &lt;script&gt;alert(2)&lt;/script&gt; at home", html);
        Assert.Contains("The clerk wrote &lt;b&gt;the year&lt;/b&gt; only.", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<illegible>", html);
        Assert.DoesNotContain("<b>the year</b>", html);
    }

    [Fact]
    public void MarkupInANoteOnAName_IsShownAsTextInThePageBody()
    {
        string html = GenerateMarkupHtml();

        Assert.Contains("Known as &lt;script&gt;alert(3)&lt;/script&gt; the elder", html);
        Assert.DoesNotContain("<script", html);
    }
}

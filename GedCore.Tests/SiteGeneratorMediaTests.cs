using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Tests for photo rendering in site generation (Subproject J): parent-card
/// portraits, a family/spouse gallery, missing-file/absolute-URL handling,
/// and staging referenced files into &lt;output&gt;/media/.
/// </summary>
public class SiteGeneratorMediaTests : IDisposable
{
    const string MinimalTemplate =
        "<html><head><title><insert title></title></head><body><insert body></body></html>";

    readonly string _mediaDir;
    readonly string _outDir;

    public SiteGeneratorMediaTests()
    {
        _mediaDir = Path.Combine(Path.GetTempPath(), "gedfire-media-src-" + Path.GetRandomFileName());
        _outDir   = Path.Combine(Path.GetTempPath(), "gedfire-media-out-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_mediaDir);
        Directory.CreateDirectory(_outDir);
    }

    public void Dispose()
    {
        Directory.Delete(_mediaDir, recursive: true);
        Directory.Delete(_outDir, recursive: true);
    }

    // A husband with a portrait (@M1@) and a second, non-image OBJE (@M2@,
    // goes to the gallery since it isn't the first link); a wife with no
    // media; one child so the family page actually gets written.
    const string Ged = """
        0 @M1@ OBJE
        1 TITL Wedding & family portrait
        1 FILE husband.jpg
        2 FORM image/jpeg
        0 @M2@ OBJE
        1 TITL Last will
        1 FILE will.pdf
        2 FORM application/pdf
        0 @I1@ INDI
        1 NAME John /Smith/
        1 SEX M
        1 BIRT
        2 DATE 1 JAN 1850
        1 OBJE @M1@
        1 OBJE @M2@
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Jane /Doe/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Mary /Smith/
        1 SEX F
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        """;

    GedModel BuildModel(string gedText = Ged) => ModelBuilder.Build(Ged55Parser.Parse(gedText));

    string FamilyHtmlFile()
    {
        var file = Directory.GetFiles(_outDir, "*.html")
            .Single(f => !Path.GetFileName(f).StartsWith("index", StringComparison.OrdinalIgnoreCase));
        return file;
    }

    void WriteMediaFile(string relativePath, string content)
    {
        string full = Path.Combine(_mediaDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Portrait_RendersFigureWithEncodedAltAndBaseUrlJoinedSrc()
    {
        WriteMediaFile("husband.jpg", "jpeg-bytes");
        WriteMediaFile("will.pdf", "pdf-bytes");

        var model = BuildModel();
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.Contains("<figure class=\"portrait\">", html);
        Assert.Contains("src=\"media/husband.jpg\"", html);
        Assert.Contains("alt=\"Wedding &amp; family portrait\"", html);
        Assert.Empty(gen.Warnings);
    }

    [Fact]
    public void NonImageFile_GoesToGalleryAsAnchorNotImg()
    {
        WriteMediaFile("husband.jpg", "jpeg-bytes");
        WriteMediaFile("will.pdf", "pdf-bytes");

        var model = BuildModel();
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.Contains("class=\"gallery\"", html);
        Assert.Contains("<a href=\"media/will.pdf\">", html);
        Assert.DoesNotContain("<img src=\"media/will.pdf\"", html);
    }

    [Fact]
    public void CitedMultilineNote_RendersHtmlLineBreaks()
    {
        var model = BuildModel(Ged.Replace("""
            1 NAME John /Smith/
            """, """
            1 NAME John /Smith/
            1 NOTE First paragraph.
            2 CONT Second paragraph.
            2 SOUR @S1@
            """) + "\n" + """
            0 @S1@ SOUR
            1 TITL Family papers
            """);
        var gen = new SiteGenerator(model, MinimalTemplate);
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.Contains("First paragraph.<br>\r\nSecond paragraph.", html);
    }

    [Fact]
    public void MultiplePersonNotes_RenderInGedcomOrder()
    {
        var model = BuildModel(Ged.Replace("""
            1 NAME John /Smith/
            """, """
            1 NAME John /Smith/
            1 NOTE First biography entry.
            1 NOTE Second biography entry.
            2 SOUR @S1@
            1 NOTE Third biography entry.
            """) + "\n" + """
            0 @S1@ SOUR
            1 TITL Family papers
            """);
        var gen = new SiteGenerator(model, MinimalTemplate);
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        int first = html.IndexOf("First biography entry.", StringComparison.Ordinal);
        int second = html.IndexOf("Second biography entry.", StringComparison.Ordinal);
        int third = html.IndexOf("Third biography entry.", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < third);
        Assert.Contains("Family papers", html);
    }

    [Fact]
    public void HtmlNote_RendersPortableFormattingAndStripsUnsafeMarkup()
    {
        var model = BuildModel(Ged.Replace("""
            1 NAME John /Smith/
            """, """
            1 NAME John /Smith/
            1 NOTE Named after <i onclick="bad()">The Odyssey</i><script>alert(1)</script><a href="https://bad.example"> safely</a>.
            2 MIME text/html
            """));
        var gen = new SiteGenerator(model, MinimalTemplate);
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.Contains("Named after <i>The Odyssey</i> safely.", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("alert(1)", html);
        Assert.DoesNotContain("https://bad.example", html);
    }

    [Fact]
    public void SharedMediaObject_LinkedFromBothSpousesAndFam_RendersOnceInGallery()
    {
        // A couple photo is typically linked from both spouses (and here the
        // FAM too): three links, one gallery figure. Each spouse's portrait
        // slot is taken by their own first link.
        const string ged = """
            0 @M1@ OBJE
            1 TITL His portrait
            1 FILE him.jpg
            2 FORM image/jpeg
            0 @M2@ OBJE
            1 TITL Couple photo
            1 FILE couple.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1850
            1 OBJE @M1@
            1 OBJE @M2@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Jane /Doe/
            1 SEX F
            1 OBJE @M2@
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            1 OBJE @M2@
            """;
        WriteMediaFile("him.jpg", "jpeg-bytes");
        WriteMediaFile("couple.jpg", "jpeg-bytes");

        var model = BuildModel(ged);
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        // Wife's portrait slot renders couple.jpg once; the husband's second
        // link and the FAM link to the same object add nothing more.
        Assert.Equal(1, CountOf(html, "src=\"media/couple.jpg\""));
        Assert.Equal(1, CountOf(html, "src=\"media/him.jpg\""));
    }

    static int CountOf(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void MissingMediaFile_WarnsAndOmitsFigure_GenerationStillSucceeds()
    {
        // husband.jpg deliberately never written to _mediaDir; will.pdf is.
        WriteMediaFile("will.pdf", "pdf-bytes");

        var model = BuildModel();
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.DoesNotContain("class=\"portrait\"", html);
        Assert.DoesNotContain("husband.jpg", html);
        Assert.Contains(gen.Warnings, w => w.Contains("@M1@") && w.Contains("husband.jpg"));
    }

    [Fact]
    public void AbsoluteUrlPayload_UsedVerbatim_NothingStaged()
    {
        const string ged = """
            0 @M1@ OBJE
            1 FILE https://example.org/husband.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 OBJE @M1@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Jane /Doe/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            """;

        var model = BuildModel(ged);
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.Contains("src=\"https://example.org/husband.jpg\"", html);
        Assert.Empty(gen.Warnings);
        Assert.False(Directory.Exists(Path.Combine(_outDir, "media")));
    }

    [Fact]
    public void Staging_CopiesReferencedFile_LeavesUnreferencedFileAlone()
    {
        WriteMediaFile("husband.jpg", "jpeg-bytes");
        WriteMediaFile("will.pdf", "pdf-bytes");
        WriteMediaFile("unreferenced.jpg", "not-referenced-by-any-op");

        var model = BuildModel();
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        Assert.Equal("jpeg-bytes", File.ReadAllText(Path.Combine(_outDir, "media", "husband.jpg")));
        Assert.Equal("pdf-bytes", File.ReadAllText(Path.Combine(_outDir, "media", "will.pdf")));
        Assert.False(File.Exists(Path.Combine(_outDir, "media", "unreferenced.jpg")));
    }

    [Fact]
    public void NoMediaDirConfigured_TreatsRelativePathAsUnresolvable_NoThrow()
    {
        var model = BuildModel();
        var gen = new SiteGenerator(model, MinimalTemplate); // MediaOptions.None
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.DoesNotContain("class=\"portrait\"", html);
        Assert.Contains(gen.Warnings, w => w.Contains("no --media-dir"));
    }

    [Fact]
    public void PathTraversalPayload_RejectedLikeMissing_NotStaged()
    {
        const string ged = """
            0 @M1@ OBJE
            1 FILE ../outside.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 OBJE @M1@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Jane /Doe/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            """;

        var model = BuildModel(ged);
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.DoesNotContain("class=\"portrait\"", html);
        Assert.Contains(gen.Warnings, w => w.Contains("escapes --media-dir"));
        Assert.False(Directory.Exists(Path.Combine(_outDir, "media")));
    }

    [Fact]
    public void LivingSpouse_ContributesNoMediaToFamilyPage()
    {
        WriteMediaFile("husband.jpg", "jpeg-bytes");

        // Husband born recently, no death-class fact -> plausibly living.
        const string ged = """
            0 @M1@ OBJE
            1 FILE husband.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1990
            1 OBJE @M1@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Jane /Doe/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            1 OBJE @M1@
            """;

        var model = BuildModel(ged);
        PrivacyFilter.Apply(model, currentYear: 2026);
        var gen = new SiteGenerator(model, MinimalTemplate, new MediaOptions(_mediaDir, "media/"));
        gen.Generate(_outDir);

        string html = File.ReadAllText(FamilyHtmlFile());
        Assert.DoesNotContain("class=\"portrait\"", html);
        Assert.DoesNotContain("class=\"gallery\"", html);
    }
}

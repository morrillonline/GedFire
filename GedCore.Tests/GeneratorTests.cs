using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Smoke tests for SiteGenerator: verify that a family page and an index page
/// are produced with the expected modern-format HTML structure.
/// Runs against the compact embedded GEDCOM 5.5 fixture derived from the
/// upstream GEDCOM 7 test file.
/// </summary>
public class GeneratorTests : IDisposable
{
    readonly string _outDir;
    readonly GedModel _model;

    const string MinimalTemplate =
        "<html><head><title><insert title></title></head>" +
        "<body>" +
        "<div class=\"ad\">Advertisement</div>" +
        "<insert body>" +
        "<div class=\"ad\">Advertisement</div>" +
        "</body></html>";

    public GeneratorTests()
    {
        _outDir = Path.Combine(Path.GetTempPath(), "gedfire-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_outDir);

        var ged = ReadResource("Example-5.5.ged");
        var doc = Ged55Parser.Read(new MemoryStream(ged));
        _model = ModelBuilder.Build(doc);

        var gen = new SiteGenerator(_model, MinimalTemplate);
        gen.Generate(_outDir);
    }

    public void Dispose() => Directory.Delete(_outDir, recursive: true);

    // -------------------------------------------------------------------------
    // Family page — ImmigrantNameJohn-2000-2022.html
    // -------------------------------------------------------------------------

    [Fact]
    public void FamilyPage_FileExists()
    {
        Assert.True(File.Exists(FamilyFile), $"Expected {FamilyFile}");
    }

    [Fact]
    public void FamilyPage_HasFamTitle()
    {
        Assert.Contains("class=\"fam-title\"", FamilyHtml);
        Assert.Contains("John Immigrant Name", FamilyHtml);
        Assert.Contains("Professional Name", FamilyHtml);
    }

    [Fact]
    public void FamilyPage_HasCoupleCards()
    {
        Assert.Contains("class=\"couple\"", FamilyHtml);
        // Two person cards
        Assert.Equal(2, CountOccurrences(FamilyHtml, "class=\"person\""));
        // Husband / Wife role labels
        Assert.Contains("Husband", FamilyHtml);
        Assert.Contains("Wife", FamilyHtml);
    }

    [Fact]
    public void FamilyPage_HasFactsLists()
    {
        Assert.Contains("dl class=\"facts\"", FamilyHtml);
        Assert.Contains("<dt>", FamilyHtml);
        Assert.Contains("<dd>", FamilyHtml);
    }

    [Fact]
    public void FamilyPage_HasChildren()
    {
        Assert.Contains("class=\"fam-children\"", FamilyHtml);
        Assert.Contains("class=\"children\"", FamilyHtml);
        Assert.Contains("class=\"child\"", FamilyHtml);
        Assert.Equal(1, CountOccurrences(FamilyHtml, "class=\"child\""));
    }

    [Fact]
    public void FamilyPage_HasBreadcrumb()
    {
        Assert.Contains("class=\"crumb\"", FamilyHtml);
        Assert.Contains("index.html", FamilyHtml); // crumb back-link
    }

    [Fact]
    public void FamilyPage_NoOldTableMarkup()
    {
        // Confirm the old table-based format is gone
        Assert.DoesNotContain("class='footnoteRef'", FamilyHtml);
        Assert.DoesNotContain("<B>CHILDREN</B>", FamilyHtml);
        Assert.DoesNotContain("<HR NOSHADE", FamilyHtml);
        Assert.DoesNotContain("class=\"tdLabel\"", FamilyHtml);
    }

    // -------------------------------------------------------------------------
    // Index page — index0.html
    // -------------------------------------------------------------------------

    [Fact]
    public void IndexPage_FileExists()
    {
        Assert.True(File.Exists(IndexFile), $"Expected {IndexFile}");
    }

    [Fact]
    public void IndexPage_HasIdxTitle()
    {
        Assert.Contains("class=\"idx-title\"", IndexHtml);
        Assert.Contains("Index of Names", IndexHtml);
    }

    [Fact]
    public void IndexPage_HasIndexTable()
    {
        Assert.Contains("class=\"index\"", IndexHtml);
        Assert.Contains("<thead>", IndexHtml);
        Assert.Contains("<tbody>", IndexHtml);
    }

    [Fact]
    public void IndexPage_SinglePageHasNoPagination()
    {
        Assert.DoesNotContain("class=\"pager\"", IndexHtml);
    }

    [Fact]
    public void IndexPage_RowsHaveNameLinks()
    {
        // Each row should have an anchor with class="nm"
        Assert.Contains("class=\"nm\"", IndexHtml);
    }

    [Fact]
    public void IndexPage_NoOldTableMarkup()
    {
        Assert.DoesNotContain("class='footnoteRef'", IndexHtml);
        Assert.DoesNotContain("<pre>", IndexHtml);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    string FamilyFile => Path.Combine(_outDir, "ImmigrantNameJohn-2000-2022.html");
    string IndexFile  => Path.Combine(_outDir, "index0.html");

    string? _familyHtml;
    string FamilyHtml => _familyHtml ??= File.ReadAllText(FamilyFile);

    string? _indexHtml;
    string IndexHtml => _indexHtml ??= File.ReadAllText(IndexFile);

    static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    static byte[] ReadResource(string fileName)
    {
        var stream = typeof(RoundTripTests).Assembly
            .GetManifestResourceStream($"GedCore.Tests.TestData.{fileName}")
            ?? throw new InvalidOperationException(
                $"Embedded resource GedCore.Tests.TestData.{fileName} not found.");
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }
}

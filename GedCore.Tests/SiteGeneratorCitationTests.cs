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
}

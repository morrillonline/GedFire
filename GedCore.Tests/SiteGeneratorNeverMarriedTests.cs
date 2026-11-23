using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>A recorded never-married answer fills the Married row of a child with no marriage, with its citation footnoted.</summary>
public class SiteGeneratorNeverMarriedTests
{
    const string Template = "<html><head><title><insert title></title></head><body><insert body></body></html>";

    static string Family(string answer, string marriage = "") => $"""
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Ruth /Holt/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Eli /Ashworth/
        1 SEX M
        {answer}
        1 FAMC @F1@
        {marriage}
        0 @S1@ SOUR
        1 TITL Salem death register
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        """;

    static string Page(string gedText)
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(gedText));
        string dir = Path.Combine(Path.GetTempPath(), "gedfire-never-married-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            return Directory.GetFiles(dir, "*.html").Select(File.ReadAllText).Single(html => html.Contains("class=\"role\">Husband<"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AChildRecordedAsNeverMarried_ShowsItWithItsCitation()
    {
        string page = Page(Family("1 NMR 0\n2 SOUR @S1@\n3 PAGE entry 40"));

        Assert.Contains("<dt>Married</dt><dd>Never married", page);
        Assert.Contains("Salem death register", page);
        Assert.Contains("entry 40", page);
    }

    [Theory]
    [InlineData("1 NMR 1")]
    [InlineData("1 OCCU Cooper")]
    public void AChildWithNoSuchAnswer_HasNoMarriedRow(string answer) =>
        Assert.DoesNotContain("<dt>Married</dt>", Page(Family(answer)));

    [Fact]
    public void AChildWithAMarriage_ShowsTheMarriageNotTheAnswer()
    {
        string page = Page(Family("1 NMR 0", "1 FAMS @F2@\n0 @I4@ INDI\n1 NAME Ann /Pike/\n1 SEX F\n1 FAMS @F2@\n0 @F2@ FAM\n1 HUSB @I3@\n1 WIFE @I4@"));

        Assert.DoesNotContain("Never married", page);
        Assert.Contains("Ann", page);
    }
}

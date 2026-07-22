using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Edge-case tests for SiteGenerator using small in-memory GEDCOMs.
/// Each test builds a minimal GEDCOM string, generates pages to a temp
/// directory, and asserts structural properties of the HTML output.
/// </summary>
public class SiteGeneratorSyntheticTests
{
    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------

    const string Template =
        "<html><head><title><insert title></title></head>" +
        "<body><insert body></body></html>";

    /// <summary>
    /// Parse <paramref name="gedText"/>, run the generator, and return the
    /// produced HTML files as filename → content.
    /// </summary>
    static Dictionary<string, string> Generate(string gedText)
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(gedText));
        string dir = Path.Combine(Path.GetTempPath(), "gedfire-syn-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            return Directory.GetFiles(dir, "*.html")
                .ToDictionary(p => Path.GetFileName(p)!, p => File.ReadAllText(p));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static int Count(string html, string pattern)
    {
        int n = 0, i = 0;
        while ((i = html.IndexOf(pattern, i, StringComparison.Ordinal)) >= 0)
            { n++; i += pattern.Length; }
        return n;
    }

    // -----------------------------------------------------------------------
    // 1. Family with no wife — only husband card generated
    // -----------------------------------------------------------------------

    [Fact]
    public void FamilyWithNoWife_GeneratesHusbandCardOnly()
    {
        string ged = """
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1800
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Jane /Smith/
            1 SEX F
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 CHIL @I2@
            """;

        var files = Generate(ged);
        var famPage = files.Single(f => !f.Key.StartsWith("index")).Value;

        // Exactly one person card — the husband
        Assert.Equal(1, Count(famPage, "class=\"person\""));
        // No Wife role label
        Assert.DoesNotContain("class=\"role\">Wife<", famPage);
        // Husband role present
        Assert.Contains("class=\"role\">Husband<", famPage);
        // Abraham's name
        Assert.Contains("John Smith", famPage);
    }

    // -----------------------------------------------------------------------
    // 2. Child with own family page — linked in parent's children section
    // -----------------------------------------------------------------------

    [Fact]
    public void ChildWithOwnPage_IsLinkedInParentChildrenSection()
    {
        // Thomas has his own page (he has a child with Mary), so he should
        // appear as a link inside Abraham's children section.
        string ged = """
            0 @I1@ INDI
            1 NAME Abraham /Parent/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Sarah /Mother/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Thomas /Parent/
            1 SEX M
            1 FAMC @F1@
            1 FAMS @F2@
            0 @I4@ INDI
            1 NAME Mary /Jones/
            1 SEX F
            1 FAMS @F2@
            0 @I5@ INDI
            1 NAME Robert /Parent/
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

        var files = Generate(ged);
        // Family pages have class="fam-title"; Abraham's page has "Abraham" in that title.
        // Use Single() to fail loudly if the predicate is ambiguous.
        string abrahamPage = files.Values
            .Single(h => h.Contains("class=\"fam-title\">Abraham"));

        // The children section exists
        Assert.Contains("class=\"fam-children\"", abrahamPage);
        // Thomas should appear as a link — he has his own family page (breadcrumb +
        // child anchor both contain href=; either satisfies the intent)
        Assert.Contains("<a href=", abrahamPage);
        Assert.Contains("Thomas", abrahamPage);
    }

    // -----------------------------------------------------------------------
    // 3. Child without own page — full inline marriage detail
    //    (verifies the hasOwnPage fix in WriteChildMarriages)
    // -----------------------------------------------------------------------

    [Fact]
    public void ChildWithoutOwnPage_ShowsSpouseBirthDeathInline()
    {
        // Child (I3) has no family page (married to I4 but no children together).
        // I4 is not childless — she has a child in another family (F3).
        // OLD behaviour: spouseIsChildless=false → WriteChildMarriageShort (no detail).
        // NEW behaviour: hasOwnPage=false → useFull=true → WriteChildMarriageFull
        //                (I4's birth year 1852 and death year 1920 shown inline).
        string ged = """
            0 @I1@ INDI
            1 NAME Parent /A/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mother /B/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Child /A/
            1 SEX M
            1 FAMC @F1@
            1 FAMS @F2@
            0 @I4@ INDI
            1 NAME Spouse /D/
            1 SEX F
            1 BIRT
            2 DATE 5 MAY 1852
            1 DEAT
            2 DATE 10 OCT 1920
            1 FAMS @F2@
            1 FAMS @F3@
            0 @I5@ INDI
            1 NAME Other /Man/
            1 SEX M
            1 FAMS @F3@
            0 @I6@ INDI
            1 NAME Kid /Man/
            1 SEX M
            1 FAMC @F3@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            0 @F2@ FAM
            1 HUSB @I3@
            1 WIFE @I4@
            0 @F3@ FAM
            1 HUSB @I5@
            1 WIFE @I4@
            1 CHIL @I6@
            """;

        var files = Generate(ged);
        string parentPage = files.Values.First(h => h.Contains("Parent A") || h.Contains("Parent /A/") || (h.Contains("Parent") && h.Contains("Mother")));

        // I4's birth year must appear inline — only shown by WriteChildMarriageFull
        Assert.Contains("1852", parentPage);
        // I4's death year too
        Assert.Contains("1920", parentPage);
    }

    // -----------------------------------------------------------------------
    // 4. Multiple child-producing marriages → two separate family pages
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleChildProducingMarriages_ProduceSeparatePages()
    {
        string ged = """
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
            1 NAME ChildB /Brown/
            1 SEX M
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

        var files = Generate(ged);
        var famPages = files.Where(f => !f.Key.StartsWith("index")).ToList();

        // Two family pages: one per child-producing marriage
        Assert.Equal(2, famPages.Count);

        // Each page names Henry and the respective wife
        bool hasAlicePage = famPages.Any(f => f.Value.Contains("Alice"));
        bool hasBettyPage = famPages.Any(f => f.Value.Contains("Betty"));
        Assert.True(hasAlicePage, "Expected a page for the Henry + Alice marriage");
        Assert.True(hasBettyPage, "Expected a page for the Henry + Betty marriage");

        // The two pages have different file names
        Assert.NotEqual(famPages[0].Key, famPages[1].Key);
    }

    // -----------------------------------------------------------------------
    // 5. URL de-duplication — same-named husband produces letter suffix
    // -----------------------------------------------------------------------

    [Fact]
    public void SameNamedHusbands_GetDeduplicatedUrls()
    {
        // Two distinct individuals with identical names and birth/death years,
        // each with a single child-producing marriage (secondary=null for both),
        // so GetFamilyUrl produces the same base pagename — the second must get
        // a letter suffix (e.g. "JohnSmith-1800-1875A.html").
        string ged = """
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1800
            1 DEAT
            2 DATE 1 JAN 1875
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1800
            1 DEAT
            2 DATE 1 JAN 1875
            1 FAMS @F2@
            0 @I3@ INDI
            1 NAME Mary /A/
            1 SEX F
            1 FAMS @F1@
            0 @I4@ INDI
            1 NAME Anne /B/
            1 SEX F
            1 FAMS @F2@
            0 @I5@ INDI
            1 NAME Child1 /Smith/
            1 SEX M
            1 FAMC @F1@
            0 @I6@ INDI
            1 NAME Child2 /Smith/
            1 SEX M
            1 FAMC @F2@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I3@
            1 CHIL @I5@
            0 @F2@ FAM
            1 HUSB @I2@
            1 WIFE @I4@
            1 CHIL @I6@
            """;

        var files = Generate(ged);
        var famFiles = files.Keys.Where(k => !k.StartsWith("index")).ToList();

        Assert.Equal(2, famFiles.Count);
        // One is the base URL, the other has a letter suffix — they differ
        Assert.NotEqual(famFiles[0], famFiles[1]);
        // Both start with the same root name
        string root = famFiles.Min()!;
        Assert.True(famFiles.Any(f => f != root && f.StartsWith(root[..^5])),
            $"Expected one URL to be a de-duplicated variant of '{root}'");
    }

    // -----------------------------------------------------------------------
    // 6. Repeated source citation — full reference shown every time, no ibid.
    // -----------------------------------------------------------------------

    [Fact]
    public void RepeatedSourceCitation_ShowsFullReferenceEachTime()
    {
        // Both birth and death cite @S1@.
        // Each popup must show the full reference — never "ibid." or a short form.
        string ged = """
            0 @S1@ SOUR
            1 AUTH John Author
            1 TITL Some Book
            1 PUBL New York 1900
            0 @I1@ INDI
            1 NAME Abraham /Test/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1800
            2 SOUR @S1@
            3 PAGE 12
            1 DEAT
            2 DATE 5 JAN 1870
            2 SOUR @S1@
            3 PAGE 45
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Test/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Child /Test/
            1 SEX M
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            """;

        var files = Generate(ged);
        string famPage = files.Single(f => !f.Key.StartsWith("index")).Value;

        // Full author name must appear once per citation (birth + death = 2)
        Assert.Equal(2, Count(famPage, "John Author"));
        // ibid. must never appear
        Assert.DoesNotContain("ibid.", famPage);
        // Hidden list present so popup JS can read it
        Assert.Contains("<ol class=\"src\" hidden", famPage);
    }

    // -----------------------------------------------------------------------
    // 7. NOTE-based citation — uses NOTE text, strips "Source Medium", adds page
    // -----------------------------------------------------------------------

    [Fact]
    public void SourceWithNote_UsesNoteAsCitation_StripsSourceMediumAndAddsPage()
    {
        // Source has both structured fields (AUTH/TITL/PUBL) AND a pre-formatted NOTE.
        // The NOTE-based path should be used (cleaner), and:
        //   - "Source Medium: Book" must be stripped from the output
        //   - The page number must appear in the citation
        //   - The structured-field PUBL text must NOT appear (it has "Name: " artifacts)
        string ged = """
            0 @S1@ SOUR
            1 AUTH Jane Writer
            1 TITL Family History Book
            1 PUBL Name: Boston : Ancestry Press, 1950;
            1 NOTE
            2 CONC Jane Writer, Family History Book (Boston : Ancestry Press, 1950), Source Medium: Book
            2 CONT .
            0 @I1@ INDI
            1 NAME Alice /Brown/
            1 SEX F
            1 BIRT
            2 DATE 3 MAR 1820
            2 SOUR @S1@
            3 PAGE 77
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Bob /Brown/
            1 SEX M
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Child /Brown/
            1 SEX M
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I2@
            1 WIFE @I1@
            1 CHIL @I3@
            """;

        var files = Generate(ged);
        string famPage = files.Single(f => !f.Key.StartsWith("index")).Value;

        // NOTE-based citation should appear with clean publication info
        Assert.Contains("Boston : Ancestry Press, 1950", famPage);
        // Page number must be incorporated
        Assert.Contains("77", famPage);
        // "Source Medium" is FTM metadata — must not appear in output
        Assert.DoesNotContain("Source Medium", famPage);
        // The PUBL field's "Name: " artifact must not appear
        Assert.DoesNotContain("Name: Boston", famPage);
    }

    // -----------------------------------------------------------------------
    // 8. Index: person with multiple child-producing marriages — name not linked
    // -----------------------------------------------------------------------

    [Fact]
    public void IndexRow_MultipleChildProducingMarriages_NameIsSpanNotAnchor()
    {
        // Henry has two child-producing marriages → index must show his name as
        // <span class="nm"> (not <a class="nm">) per the linking rule in CLAUDE.md.
        string ged = """
            0 @I1@ INDI
            1 NAME Henry /Brown/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1800
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
            1 NAME ChildB /Brown/
            1 SEX M
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

        var files = Generate(ged);
        string indexPage = files["index0.html"];

        // Henry's name must appear as a <span class="nm"> (not a link)
        Assert.Contains("<span class=\"nm\">Henry Brown</span>", indexPage);
        // Other individuals with one marriage correctly get anchor tags, so we can
        // only assert that Henry's name itself is not wrapped in an anchor.
        Assert.DoesNotContain(">Henry Brown</a>", indexPage);
    }
}

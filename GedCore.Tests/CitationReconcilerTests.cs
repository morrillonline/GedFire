using GedCore.Apply;

namespace GedCore.Tests;

// CitationReconciler is internal, so it is exercised through ChangesetApplier:
// the citation ops state how a fact's citations should read and the applier
// works out what to update or add.
public class CitationReconcilerTests : ApplyTestBase
{
    static List<string> LinesWithBirthCitations(params (string Source, string? Page, int? Quay)[] citations)
    {
        var lines = BaseLines.ToList();
        int at = lines.IndexOf("2 PLAC Minnesota") + 1;
        var added = new List<string>();
        foreach (var (source, page, quay) in citations)
        {
            added.Add($"2 SOUR {source}");
            if (page is not null) added.Add($"3 PAGE {page}");
            if (quay is not null) added.Add($"3 QUAY {quay}");
        }
        lines.InsertRange(at, added);
        int trlr = lines.IndexOf("0 TRLR");
        lines.InsertRange(trlr, ["0 @S00002@ SOUR", "1 TITL Second source"]);
        return lines;
    }

    static string CiteOp(params (string Source, string? Page, int? Quay)[] citations) =>
        $$"""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
            "citations": [ {{string.Join(", ", citations.Select(Json))}} ] } ] } ] }
        """;

    static string Json((string Source, string? Page, int? Quay) c) =>
        "{ \"source\": \"" + c.Source + "\"" +
        (c.Page is null ? "" : $", \"page\": \"{c.Page}\"") +
        (c.Quay is null ? "" : $", \"quay\": {c.Quay}") + " }";

    List<GedRecord> BirthCitations(string? source = null) =>
        [.. ReadDoc().ByXref["@I00001@"].ChildrenByTag("BIRT").Single().ChildrenByTag("SOUR")
            .Where(s => source is null || s.Value == source)];

    static string? Page(GedRecord citation) => citation.FirstChild("PAGE")?.Value;

    [Fact]
    public void StatedPage_OnTheOnlyCitationOfASource_CorrectsThePageInPlace()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", 1)));

        var result = RunExpectSuccess(CiteOp(("@S00001@", "121", null)));

        var citation = Assert.Single(BirthCitations());
        Assert.Equal("121", Page(citation));
        Assert.Equal("1", citation.FirstChild("QUAY")!.Value);
        Assert.Contains(result.Log, l => l.Contains("PAGE '112' → '121'"));
    }

    [Fact]
    public void StatedSamePage_UpdatesOtherFieldsWithoutTouchingThePage()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", 1)));

        RunExpectSuccess(CiteOp(("@S00001@", "112", 3)));

        var citation = Assert.Single(BirthCitations());
        Assert.Equal("112", Page(citation));
        Assert.Equal("3", citation.FirstChild("QUAY")!.Value);
    }

    [Fact]
    public void NoPageStated_LeavesTheExistingPageAlone()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", 1)));

        RunExpectSuccess(CiteOp(("@S00001@", null, 3)));

        var citation = Assert.Single(BirthCitations());
        Assert.Equal("112", Page(citation));
        Assert.Equal("3", citation.FirstChild("QUAY")!.Value);
    }

    [Fact]
    public void AnAdditionalPageOfACitedSource_IsAddedAsASecondCitation()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", 1)));

        RunExpectSuccess(CiteOp(("@S00001@", "112", null), ("@S00001@", "130", null)));

        Assert.Equal(["112", "130"], BirthCitations("@S00001@").Select(Page));
    }

    [Fact]
    public void TwoPagesOfOneSource_OnAFactThatDoesNotCiteItYet_AreBothAdded()
    {
        WriteFile(LinesWithBirthCitations());

        RunExpectSuccess(CiteOp(("@S00001@", "112", null), ("@S00001@", "130", 2)));

        Assert.Equal(["112", "130"], BirthCitations("@S00001@").Select(Page));
    }

    [Fact]
    public void StatingEveryExistingPage_UpdatesEachMatchingCitation()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", 1), ("@S00001@", "130", 1)));

        RunExpectSuccess(CiteOp(("@S00001@", "112", 3), ("@S00001@", "130", 2)));

        var citations = BirthCitations();
        Assert.Equal(["112", "130"], citations.Select(Page));
        Assert.Equal(["3", "2"], citations.Select(c => c.FirstChild("QUAY")!.Value));
    }

    [Fact]
    public void ManyExistingCitations_WithOneStatedThatMatchesNone_IsRejectedNamingThePages()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", null), ("@S00001@", "130", null)));
        byte[] before = ReadBytes();

        var result = Run(CiteOp(("@S00001@", "121", null)));

        Assert.False(result.Success);
        string errors = string.Join("; ", result.Errors);
        Assert.Contains("already cites it 2 time(s)", errors);
        Assert.Contains("\"112\", \"130\"", errors);
        Assert.Equal(before, ReadBytes());
    }

    [Fact]
    public void TwoStatedCitationsOfOneSourceWithTheSamePage_AreRejectedAsDuplicates()
    {
        WriteFile(LinesWithBirthCitations());

        var result = Run(CiteOp(("@S00001@", "112", null), ("@S00001@", "112", 2)));

        Assert.False(result.Success);
        Assert.Contains("cited twice on one structure with the same page (112)", string.Join("; ", result.Errors));
    }

    [Fact]
    public void RestatingTheSameCitation_IsANoOp()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", 2)));

        var result = RunExpectSuccess(CiteOp(("@S00001@", "112", 2)));

        Assert.Contains(result.Log, l => l.Contains("no-op (already cited identically)"));
    }

    [Fact]
    public void CorrectingOnePersonsPage_LeavesEveryOtherCitationOfTheSourceUntouched()
    {
        var lines = LinesWithBirthCitations(("@S00001@", "112", 1));
        int nellieCensus = lines.IndexOf("2 DATE 1930") + 2;
        lines.InsertRange(nellieCensus, ["2 SOUR @S00001@", "3 PAGE 5"]);
        WriteFile(lines);
        string nellieBefore = string.Join("|", NellieCensus().Select(n => n.Value + Page(n)));
        string sourceBefore = string.Join("|", ReadDoc().ByXref["@S00001@"].Children.Select(c => c.Tag + c.Value));

        RunExpectSuccess(CiteOp(("@S00001@", "121", null)));

        Assert.Equal("121", Page(Assert.Single(BirthCitations())));
        Assert.Equal(nellieBefore, string.Join("|", NellieCensus().Select(n => n.Value + Page(n))));
        Assert.Equal(sourceBefore, string.Join("|", ReadDoc().ByXref["@S00001@"].Children.Select(c => c.Tag + c.Value)));
    }

    IEnumerable<GedRecord> NellieCensus() =>
        ReadDoc().ByXref["@I00003@"].ChildrenByTag("CENS").SelectMany(c => c.ChildrenByTag("SOUR"));

    [Fact]
    public void DeleteCitation_WithPage_RemovesOnlyThatPagesCitation()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", null), ("@S00001@", "130", null)));

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@S00001@", "page": "112" } ] } ] }
            """);

        Assert.Equal("130", Page(Assert.Single(BirthCitations())));
    }

    [Fact]
    public void DeleteCitation_WithoutPage_WhenSeveralArePresent_IsRejectedNamingThePages()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", null), ("@S00001@", "130", null)));
        byte[] before = ReadBytes();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@S00001@" } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains("is cited 2 times (pages: \"112\", \"130\")", string.Join("; ", result.Errors));
        Assert.Equal(before, ReadBytes());
    }

    [Fact]
    public void DeleteCitation_WithAPageNobodyCites_IsANoOp()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", null)));

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@S00001@", "page": "999" } ] } ] }
            """);

        Assert.Contains(result.Log, l => l.Contains("no-op") && l.Contains("999"));
        Assert.Single(BirthCitations());
    }

    [Fact]
    public void DeleteCitation_WithoutPage_WhenOneIsPresent_StillRemovesIt()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "112", null)));

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@S00001@" } ] } ] }
            """);

        Assert.Empty(BirthCitations());
    }

    // ---- relationship provenance: a link op never rewrites an existing FAM citation; a differing page is added beside it ----

    List<string> LinesWithFamilyCitation(string family, string page, string text)
    {
        var lines = BaseLines.ToList();
        lines.InsertRange(lines.IndexOf($"0 {family} FAM") + 1,
            ["1 SOUR @S00001@", $"2 PAGE {page}", "2 DATA", $"3 TEXT {text}"]);
        return lines;
    }

    List<GedRecord> FamilyCitations(string family) => [.. ReadDoc().ByXref[family].ChildrenByTag("SOUR")];

    static string? DataText(GedRecord citation) => citation.FirstChild("DATA")?.FirstChild("TEXT")?.Value;

    [Fact]
    public void ChildLink_CitingADifferentPageOfTheFamilysSource_AddsASecondCitation()
    {
        WriteFile(LinesWithFamilyCitation("@F00002@", "p89 n25", "Edward's register entry"));

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "people": [ { "xref": "@NewI1@", "name": "Junior /Test/", "sex": "M" } ], "ops": [
              { "op": "createOrUpdateChild", "family": "@F00002@",
                "child": "@NewI1@",
                "familyCitations": [{ "source": "@S00001@", "page": "p91 n12", "dataText": "Junior's register entry", "quay": 2 }] } ] } ] }
            """);

        var citations = FamilyCitations("@F00002@");
        Assert.Equal(["p89 n25", "p91 n12"], citations.Select(Page));
        Assert.Equal("Edward's register entry", DataText(citations[0]));
        Assert.Equal("Junior's register entry", DataText(citations[1]));
        Assert.Contains(result.Log, l => l.Contains("cited @S00001@ (p91 n12)"));
        Assert.DoesNotContain(result.Log, l => l.Contains("updated citation"));
    }

    [Fact]
    public void SpouseLink_CitingADifferentPageOfTheFamilysSource_AddsASecondCitation()
    {
        WriteFile(LinesWithFamilyCitation("@F00002@", "p89 n25", "Edward's register entry"));

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@", "family": "@F00002@",
                "familyCitations": [ { "source": "@S00001@", "page": "p91 n12", "dataText": "the couple's register entry", "quay": 2 } ] } ] } ] }
            """);

        var citations = FamilyCitations("@F00002@");
        Assert.Equal(["p89 n25", "p91 n12"], citations.Select(Page));
        Assert.Equal("Edward's register entry", DataText(citations[0]));
        Assert.DoesNotContain(result.Log, l => l.Contains("updated citation"));
    }

    [Fact]
    public void ParentLink_CitingADifferentPageOfTheFamilysSource_AddsASecondCitation()
    {
        WriteFile(LinesWithFamilyCitation("@F00001@", "p89 n25", "Edward's register entry"));

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00001@", "role": "father", "parent": "@I00002@",
                "familyCitations": [{ "source": "@S00001@", "page": "p91 n12", "dataText": "son of Harvey", "quay": 2 }] } ] } ] }
            """);

        var citations = FamilyCitations("@F00001@");
        Assert.Equal(["p89 n25", "p91 n12"], citations.Select(Page));
        Assert.Equal("Edward's register entry", DataText(citations[0]));
        Assert.DoesNotContain(result.Log, l => l.Contains("updated citation"));
    }

    [Fact]
    public void ChildLink_RestatingTheSamePage_UpdatesThatCitationInPlace()
    {
        WriteFile(LinesWithFamilyCitation("@F00002@", "p89 n25", "Edward's register entry"));

        RunExpectSuccess("""
            { "items": [ { "item": 1, "people": [ { "xref": "@NewI1@", "name": "Junior /Test/", "sex": "M" } ], "ops": [
              { "op": "createOrUpdateChild", "family": "@F00002@",
                "child": "@NewI1@",
                "familyCitations": [{ "source": "@S00001@", "page": "p89 n25", "quay": 2 }] } ] } ] }
            """);

        var citation = Assert.Single(FamilyCitations("@F00002@"));
        Assert.Equal("p89 n25", Page(citation));
        Assert.Equal("Edward's register entry", DataText(citation));
        Assert.Equal("2", citation.FirstChild("QUAY")!.Value);
    }

    // ---- commentary ----

    static string CommentOp(string citationJson) =>
        $$"""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
            "citations": [ {{citationJson}} ] } ] } ] }
        """;

    static string? Commentary(GedRecord citation) => citation.FirstChild("NOTE")?.FullValue();

    [Fact]
    public void Commentary_IsWrittenAsTheCitationsNoteAfterItsOtherFields_AndReapplyingChangesNothing()
    {
        WriteBaseFile();
        string op = CommentOp("""{ "source": "@S00001@", "page": "p. 4", "quay": 2, "commentary": "The register gives his age, not his birth date." }""");

        RunExpectSuccess(op);
        var bytes = ReadBytes();
        var second = RunExpectSuccess(op);

        var citation = Assert.Single(BirthCitations());
        Assert.Equal(["PAGE", "QUAY", "NOTE"], citation.Children.Select(c => c.Tag));
        Assert.Equal("The register gives his age, not his birth date.", Commentary(citation));
        Assert.Null(second.OutputBytes);
        Assert.Equal(bytes, ReadBytes());
    }

    [Fact]
    public void Commentary_OnAnExistingCitation_IsAddedThenReplaced()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "p. 4", 2)));

        var added = RunExpectSuccess(CommentOp("""{ "source": "@S00001@", "page": "p. 4", "commentary": "First reading." }"""));
        var replaced = RunExpectSuccess(CommentOp("""{ "source": "@S00001@", "page": "p. 4", "commentary": "Second reading." }"""));

        var citation = Assert.Single(BirthCitations());
        Assert.Equal("Second reading.", Commentary(citation));
        Assert.Single(citation.ChildrenByTag("NOTE"));
        Assert.Equal("2", citation.FirstChild("QUAY")!.Value);
        Assert.Contains(added.Log, l => l.Contains("commentary added"));
        Assert.Contains(replaced.Log, l => l.Contains("commentary replaced"));
    }

    [Fact]
    public void CommentaryAlone_CitesVoid_IsKeptOnceAndAnotherIsAddedBeside()
    {
        WriteFile(LinesWithBirthCitations(("@S00001@", "p. 4", 2)));
        string first = CommentOp("""{ "source": "@VOID@", "commentary": "Both censuses round his age to the decade." }""");

        RunExpectSuccess(first);
        var replay = RunExpectSuccess(first);
        RunExpectSuccess(CommentOp("""{ "source": "@VOID@", "commentary": "His stone gives no year." }"""));

        var comments = BirthCitations("@VOID@");
        Assert.Equal(["Both censuses round his age to the decade.", "His stone gives no year."], comments.Select(Commentary));
        Assert.All(comments, c => Assert.Equal(["NOTE"], c.Children.Select(x => x.Tag)));
        Assert.Single(BirthCitations("@S00001@"));
        Assert.Null(replay.OutputBytes);
    }

    [Theory]
    [InlineData("""{ "source": "@VOID@", "page": "p. 1", "commentary": "x" }""")]
    [InlineData("""{ "source": "@VOID@", "quay": 1, "commentary": "x" }""")]
    [InlineData("""{ "source": "@VOID@", "dataText": "x", "commentary": "x" }""")]
    public void CommentaryAlone_WithAPageExtractOrGrade_IsRefused(string citation)
    {
        WriteBaseFile();

        var result = Run(CommentOp(citation), dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("holds only commentary"));
    }

    [Fact]
    public void CommentaryAlone_DoesNotSatisfyAMarriagesRequiredCitation()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@", "spouse": "@I00003@", "family": "@F00001@",
                "marriage": { "date": "1925", "citations": [ { "source": "@VOID@", "commentary": "Family tradition only." } ] } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("commentary alone cites no source"));
    }

    [Fact]
    public void DeleteCitation_RemovesCommentaryNamedByItsText_AndNeedsTheText()
    {
        WriteBaseFile();
        RunExpectSuccess(CommentOp("""{ "source": "@VOID@", "commentary": "Keep this one." }"""));
        RunExpectSuccess(CommentOp("""{ "source": "@VOID@", "commentary": "Remove this one." }"""));
        const string unnamed = """
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@VOID@" } ] } ] }
            """;

        var refused = Run(unnamed, dryRun: true);
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@VOID@", "commentary": "Remove this one." } ] } ] }
            """);

        Assert.False(refused.Success);
        Assert.Contains(refused.Errors, e => e.Contains("selected by its \"commentary\" text"));
        Assert.Equal(["Keep this one."], BirthCitations("@VOID@").Select(Commentary));
    }
}

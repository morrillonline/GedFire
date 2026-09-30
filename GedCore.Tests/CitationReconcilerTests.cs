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
}

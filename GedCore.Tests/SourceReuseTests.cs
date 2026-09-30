using GedCore.Apply;

namespace GedCore.Tests;

// SourceMatcher is internal, so reuse is exercised through ChangesetApplier:
// a new source that describes a document already in the file is not created
// again.
public class SourceReuseTests : ApplyTestBase
{
    static List<string> LinesWithCensusSource(string? publication = null)
    {
        var lines = BaseLines.ToList();
        int trlr = lines.IndexOf("0 TRLR");
        var source = new List<string> { "0 @S00002@ SOUR", "1 AUTH US Census Bureau", "1 TITL 1850 Census" };
        if (publication is not null) source.Add($"1 PUBL {publication}");
        lines.InsertRange(trlr, source);
        return lines;
    }

    static string NewSourceChangeset(string title, string author, string extra = "") =>
        $$"""
        { "newSources": [ { "xref": "@NewS1@", "ops": [
            { "op": "createOrUpdateSource", "xref": "@NewS1@", "title": "{{title}}", "auth": "{{author}}"{{extra}} } ] } ],
          "items": [ { "item": 1, "ops": [
            { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
              "citation": { "source": "@NewS1@", "page": "p. 7" } } ] } ] }
        """;

    string SourceText(string xref) =>
        string.Join("|", ReadDoc().ByXref[xref].Children.Select(c => c.Tag + "=" + c.FullValue()));

    [Fact]
    public void NewSource_ThatIsTheSameDocumentAsAnExistingOne_ReusesTheExistingSource()
    {
        WriteFile(LinesWithCensusSource());
        string before = SourceText("@S00002@");

        var result = RunExpectSuccess(NewSourceChangeset("  1850   CENSUS ", "us census bureau"));

        Assert.Equal("@S00002@", result.MintedXrefs["@NewS1@"]);
        Assert.False(result.Deltas.ContainsKey("SOUR"));
        Assert.Equal(2, ReadDoc().Records.Count(r => r.Tag == "SOUR"));
        var citation = ReadDoc().ByXref["@I00001@"].ChildrenByTag("BIRT").Single().ChildrenByTag("SOUR").Single();
        Assert.Equal("@S00002@", citation.Value);
        Assert.Equal("p. 7", citation.FirstChild("PAGE")!.Value);
        Assert.Equal(before, SourceText("@S00002@"));
    }

    [Fact]
    public void ReusedSource_IsNotModifiedEvenWhenTheOpSuppliesMoreFields()
    {
        WriteFile(LinesWithCensusSource());
        string before = SourceText("@S00002@");

        RunExpectSuccess(NewSourceChangeset("1850 Census", "US Census Bureau",
            ", \"url\": \"https://example.test/census\", \"accessed\": \"1 Jan 2026\""));

        Assert.Equal(before, SourceText("@S00002@"));
    }

    [Fact]
    public void NewSource_WithADifferentAuthor_IsCreated()
    {
        WriteFile(LinesWithCensusSource());

        var result = RunExpectSuccess(NewSourceChangeset("1850 Census", "State of Maine"));

        Assert.NotEqual("@S00002@", result.MintedXrefs["@NewS1@"]);
        Assert.Equal(1, result.Deltas["SOUR"]);
    }

    [Fact]
    public void NewSource_WhenTheExistingOneRecordsAPublication_IsCreatedBecauseTheyDiffer()
    {
        WriteFile(LinesWithCensusSource(publication: "Washington, 1853"));

        var result = RunExpectSuccess(NewSourceChangeset("1850 Census", "US Census Bureau"));

        Assert.NotEqual("@S00002@", result.MintedXrefs["@NewS1@"]);
        Assert.Equal(1, result.Deltas["SOUR"]);
    }

    [Fact]
    public void TwoPlaceholdersDescribingOneNewDocument_CollapseToOneSource()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "newSources": [
                { "xref": "@NewS1@", "ops": [ { "op": "createOrUpdateSource", "xref": "@NewS1@", "title": "Pension file 4417", "auth": "US Pension Bureau" } ] },
                { "xref": "@NewS2@", "ops": [ { "op": "createOrUpdateSource", "xref": "@NewS2@", "title": "pension file 4417", "auth": "US Pension Bureau" } ] } ],
              "items": [ { "item": 1, "ops": [
                { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                  "citation": { "source": "@NewS1@", "page": "a" } },
                { "op": "createOrUpdateCitation", "record": "@I00003@", "fact": "CENS", "match": { "date": "1930" },
                  "citation": { "source": "@NewS2@", "page": "b" } } ] } ] }
            """);

        Assert.Equal(result.MintedXrefs["@NewS1@"], result.MintedXrefs["@NewS2@"]);
        Assert.Equal(1, result.Deltas["SOUR"]);
    }
}

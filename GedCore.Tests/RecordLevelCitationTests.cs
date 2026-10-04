namespace GedCore.Tests;

/// <summary>
/// createOrUpdateCitation / deleteCitation without "fact": they address a
/// family record's own citations, the provenance the spouse, child and parent
/// ops attach on the FAM.
/// </summary>
public class RecordLevelCitationTests : ApplyTestBase
{
    // @F00001@ carries a family-level citation whose quoted text names an id,
    // as ones written before the prose check did.
    private void WriteFileWithDirtyFamilyCitation() =>
        WriteFile([.. BaseLines.SelectMany(l => l == "1 CHIL @I00001@"
            ? new[] { l, "1 SOUR @S00001@", "2 PAGE p. 1", "2 DATA", "3 TEXT matches @I00002@ exactly" }
            : new[] { l })]);

    private static string FamilyCitationOf(GedDocument doc) =>
        doc.ByXref["@F00001@"].ChildrenByTag("SOUR").Single().FirstChild("DATA")!.FirstChild("TEXT")!.Value;

    [Fact]
    public void Create_CorrectsTheQuotedTextOfAFamilyCitation()
    {
        WriteFileWithDirtyFamilyCitation();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@F00001@",
                "citation": { "source": "@S00001@", "page": "p. 1", "dataText": "matches Harvey Test exactly" } } ] } ] }
            """);

        Assert.Equal("matches Harvey Test exactly", FamilyCitationOf(ReadDoc()));
        Assert.Contains(result.Log, l => l.StartsWith("createOrUpdateCitation on @F00001@ (record level):"));
    }

    [Fact]
    public void Create_RerunIsANoOp()
    {
        WriteFileWithDirtyFamilyCitation();
        const string json = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@F00001@",
                "citation": { "source": "@S00001@", "page": "p. 1", "dataText": "matches Harvey Test exactly" } } ] } ] }
            """;
        RunExpectSuccess(json);
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(json);

        Assert.Contains(second.Log, l => l.Contains("no-op (already cited identically)"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void Create_AddsACitationWhenTheSourceIsNotYetCitedOnTheFamily()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@F00001@",
                "citation": { "source": "@S00001@", "page": "p. 9", "dataText": "family listed" } } ] } ] }
            """);

        var citation = ReadDoc().ByXref["@F00001@"].ChildrenByTag("SOUR").Single();
        Assert.Equal("@S00001@", citation.Value);
        Assert.Equal("p. 9", citation.FirstChild("PAGE")!.Value);
    }

    [Fact]
    public void Create_ResolvesAFamilyPlaceholderCreatedEarlierInTheSameChangeset()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@NewF1@", "husb": "@I00002@",
                "child": { "xref": "@NewI1@", "name": "Junior /Test/", "sex": "M" },
                "citation": { "source": "@S00001@", "page": "a", "dataText": "child of Harvey", "quay": 2 } },
              { "op": "createOrUpdateCitation", "record": "@NewF1@",
                "citation": { "source": "@S00001@", "page": "a", "dataText": "child of Harvey", "quay": 2 } } ] } ] }
            """);

        var family = ReadDoc().ByXref["@F00004@"];
        Assert.Equal("child of Harvey",
            family.ChildrenByTag("SOUR").Single().FirstChild("DATA")!.FirstChild("TEXT")!.Value);   // one citation, not two
    }

    [Fact]
    public void Create_WithoutFactOnAPerson_IsRefused()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@",
                "citation": { "source": "@S00001@", "page": "p. 1" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("(record level)") && e.Contains("is not a family"));
    }

    [Fact]
    public void Create_WithMatchButNoFact_IsRefused()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@F00001@", "match": { "date": "1900" },
                "citation": { "source": "@S00001@", "page": "p. 1" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("\"match\" selects a fact"));
    }

    [Fact]
    public void Create_StillRefusesAnIdInTheCorrectedText()
    {
        WriteFileWithDirtyFamilyCitation();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@F00001@",
                "citation": { "source": "@S00001@", "page": "p. 1", "dataText": "still names @I00003@" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("internal id @I00003@"));
    }

    [Fact]
    public void Delete_RemovesTheFamilyCitation()
    {
        WriteFileWithDirtyFamilyCitation();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@F00001@", "source": "@S00001@" } ] } ] }
            """);

        Assert.Empty(ReadDoc().ByXref["@F00001@"].ChildrenByTag("SOUR"));
        Assert.Contains(result.Log, l => l == "deleteCitation on @F00001@ (record level): removed citation @S00001@");
    }

    [Fact]
    public void Delete_WhenAbsent_IsANoOp()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@F00001@", "source": "@S00001@" } ] } ] }
            """);

        Assert.Contains(result.Log, l => l.Contains("no-op (@S00001@ not cited)"));
    }

    [Fact]
    public void Delete_WithSeveralPagesNeedsThePage()
    {
        WriteFile([.. BaseLines.SelectMany(l => l == "1 CHIL @I00001@"
            ? new[] { l, "1 SOUR @S00001@", "2 PAGE p. 1", "1 SOUR @S00001@", "2 PAGE p. 2" }
            : new[] { l })]);

        var ambiguous = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@F00001@", "source": "@S00001@" } ] } ] }
            """);
        Assert.False(ambiguous.Success);
        Assert.Contains(ambiguous.Errors, e => e.Contains("add \"page\""));

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@F00001@", "source": "@S00001@", "page": "p. 2" } ] } ] }
            """);
        Assert.Equal("p. 1", ReadDoc().ByXref["@F00001@"].ChildrenByTag("SOUR").Single().FirstChild("PAGE")!.Value);
    }
}

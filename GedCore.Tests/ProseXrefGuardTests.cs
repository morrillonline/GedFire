using GedCore.Apply;

namespace GedCore.Tests;

/// <summary>
/// Prose written by a changeset must name people and sources, never carry
/// internal record ids. Exercised through the applier, which every changeset
/// tool shares for validation.
/// </summary>
public class ProseXrefGuardTests : ApplyTestBase
{
    private ApplyResult RunOps(string ops)
    {
        WriteBaseFile();
        return Run($$"""{ "items": [ { "item": 1, "ops": [ {{ops}} ] } ] }""");
    }

    private static string Citation(string page = "p. 1", string dataText = "extract") =>
        $$"""{ "source": "@S00001@", "page": "{{page}}", "dataText": "{{dataText}}", "quay": 2 }""";

    private void AssertRefused(ApplyResult result, string id, string field)
    {
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(id) && e.Contains(field) && e.Contains("internal id"));
    }

    [Theory]
    [InlineData("@S00223@")]
    [InlineData("@I00001@")]
    [InlineData("@F00002@")]
    [InlineData("@NewI1@")]
    [InlineData("@VOID@")]
    public void NoteText_WithAnInternalId_IsRefused(string id)
    {
        var result = RunOps($$"""
            { "op": "createOrUpdateNote", "record": "@I00001@", "text": "Per {{id}} he was a miller." }
            """);

        AssertRefused(result, id, "text");
    }

    [Fact]
    public void NoteText_WithAnId_LeavesTheFileUntouched()
    {
        WriteBaseFile();
        var before = ReadBytes();

        Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "text": "See @S00001@." } ] } ] }
            """);

        Assert.Equal(before, ReadBytes());
    }

    [Fact]
    public void CitationDataText_WithAnInternalId_IsRefused()
    {
        var result = RunOps($$"""
            { "op": "createOrUpdateVital", "record": "@F00001@", "fact": "MARR",
              "value": { "date": "1922" }, "citation": {{Citation(dataText: "see @I00002@")}} }
            """);

        AssertRefused(result, "@I00002@", "dataText");
    }

    [Fact]
    public void CitationPage_WithAnInternalId_IsRefused()
    {
        var result = RunOps($$"""
            { "op": "createOrUpdateVital", "record": "@F00001@", "fact": "MARR",
              "value": { "date": "1922" }, "citation": {{Citation(page: "@S00001@ p. 4")}} }
            """);

        AssertRefused(result, "@S00001@", "page");
    }

    [Fact]
    public void InlinePersonFactCitation_WithAnInternalId_IsRefused()
    {
        var result = RunOps($$"""
            { "op": "createOrUpdateChild", "family": "@F00002@",
              "child": { "xref": "@NewI1@", "name": "Junior /Test/", "sex": "M",
                         "facts": [ { "fact": "BIRT", "value": { "date": "1951" },
                                      "citation": {{Citation(dataText: "child of @I00001@")}} } ] } }
            """);

        AssertRefused(result, "@I00001@", "dataText");
    }

    [Fact]
    public void SourceTitleAndAuthor_WithAnInternalId_AreRefused()
    {
        var result = RunOps("""
            { "op": "createOrUpdateSource", "xref": "@NewSource1@",
              "title": "Register kept by @I00002@", "auth": "Clerk @I00003@" }
            """);

        AssertRefused(result, "@I00002@", "title");
        AssertRefused(result, "@I00003@", "auth");
    }

    [Fact]
    public void SpouseNote_WithAnInternalId_IsRefused()
    {
        var result = RunOps($$"""
            { "op": "createOrUpdateSpouse", "person": "@I00002@", "spouse": "@I00003@", "family": "@F00001@",
              "note": "Same family as @F00002@", "citation": {{Citation()}} }
            """);

        AssertRefused(result, "@F00002@", "note");
    }

    [Fact]
    public void VitalTextValue_WithAnInternalId_IsRefused()
    {
        var result = RunOps($$"""
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
              "value": "Allen /Test/ called @I00002@", "citation": {{Citation()}} }
            """);

        AssertRefused(result, "@I00002@", "value");
    }

    [Fact]
    public void MediaTitle_WithAnInternalId_IsRefused()
    {
        var result = RunOps("""
            { "op": "createOrUpdateMedia", "title": "Portrait of @I00001@",
              "files": [ { "path": "portrait.jpg", "mediaType": "image/jpeg" } ] }
            """);

        AssertRefused(result, "@I00001@", "title");
    }

    [Fact]
    public void MergeNote_WithAnInternalId_IsRefused()
    {
        var result = RunOps("""
            { "op": "mergePerson", "survivor": "@I00003@", "duplicate": "@I00004@",
              "note": "Merged duplicate @I00004@ into this record." }
            """);

        AssertRefused(result, "@I00004@", "note");
    }

    [Fact]
    public void NoteText_WithEscapedAtSignOrEmailOrNames_IsAccepted()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Harvey Test, per the Existing source; contact a@b.example or @home, 5 @ noon." } ] } ] }
            """);
    }

    [Fact]
    public void DeleteNoteAndMatch_MayStillNameALegacyIdInTheExistingText()
    {
        WriteFile([.. BaseLines.Take(BaseLines.Length - 1), "0 @I00009@ INDI", "1 NAME Old /Note/",
                   "1 NOTE Legacy text citing @S00001@.", "0 TRLR"]);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00009@",
                "match": "Legacy text citing @S00001@.", "text": "Legacy text citing the Existing source." } ] },
              { "item": 2, "ops": [
              { "op": "deleteNote", "record": "@I00009@", "text": "Legacy text citing the Existing source." } ] } ] }
            """);
    }
}

using System.Text.Json;
using GedCore.Ged70;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

/// <summary>
/// The after argument of validate_changeset and check_plausibility: earlier changesets applied in memory, in order,
/// before the target is checked. The tools read real files, so these tests use temporary ones.
/// </summary>
public class PriorChangesetsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-prior-changesets-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    const string Ged = "0 HEAD\r\n1 GEDC\r\n2 VERS 7.0\r\n0 @I1@ INDI\r\n1 NAME Allen /Test/\r\n1 SEX M\r\n0 TRLR\r\n";

    // Another approved package: two census facts on Allen.
    const string TwoCensuses = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I1@", "fact": "CENS", "mode": "add", "value": { "date": "1910", "place": "Maine" } },
          { "op": "createOrUpdateVital", "record": "@I1@", "fact": "CENS", "mode": "add", "value": { "date": "1920", "place": "Maine" } } ] } ] }
        """;

    // A census drafted while Allen had none: a plain upsert, as drafts were written before repeatable facts were added.
    const string PlainCensus = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I1@", "fact": "CENS", "value": { "date": "1900", "place": "Maine" } } ] } ] }
        """;

    string Write(string name, string text)
    {
        string path = Path.Combine(_dir, name);
        if (name.EndsWith(".ged", StringComparison.Ordinal))
        {
            using var stream = File.Create(path);
            Ged70Formatter.Write(Ged70Parser.Parse(text), stream);
        }
        else File.WriteAllText(path, text);
        return path;
    }

    static JsonElement After(params string[] paths) =>
        JsonSerializer.SerializeToElement(paths.Select(p => new { changesetPath = p, items = "all" }));

    [Fact]
    public async Task TheVerificationPolicy_ReadsTheDocumentAsTheEarlierChangesetsLeaveIt()
    {
        string ged = Write("tree.ged", Ged.Replace("0 TRLR\r\n", "0 @S1@ SOUR\r\n1 TITL Register\r\n0 TRLR\r\n"));
        string noted = Write("noted.json", """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I1@", "text": "Needs independent verification: the birth rests on one register." } ] } ] }
            """);
        string birth = Write("birth.json", """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1900",
                "citations": [ { "source": "@S1@", "page": "p. 4", "quay": 1 } ] } ] } ] }
            """);
        var policy = GedCore.Policy.ChangesetPolicy.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""")));
        var tool = new ValidateChangesetTool(ged, new ToolGate(), policy);

        var alone = await tool.HandleAsync(new ValidateChangesetArguments(birth, "all"), CancellationToken.None);
        var afterNote = await tool.HandleAsync(new ValidateChangesetArguments(birth, "all", After: After(noted)), CancellationToken.None);

        Assert.Contains(Errors(alone), e => e.StartsWith("policy: item 1: the BIRT on @I1@"));
        Assert.True(afterNote.StructuredContent!.Value.GetProperty("success").GetBoolean(), string.Join("; ", Errors(afterNote)));
    }

    async Task<CallToolResult> Validate(string ged, string changeset, JsonElement after) =>
        await new ValidateChangesetTool(ged, new ToolGate()).HandleAsync(new ValidateChangesetArguments(changeset, "all", After: after), CancellationToken.None);

    async Task<CallToolResult> CheckPlausibility(string ged, string changeset, JsonElement after) =>
        await new CheckPlausibilityTool(ged, new ToolGate()).HandleAsync(changeset, "all", after, CancellationToken.None);

    static IEnumerable<string> Errors(CallToolResult result) =>
        result.StructuredContent!.Value.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!);

    [Fact]
    public async Task AChangesetThatPassesAlone_FailsAfterAnEarlierOneItConflictsWith_InBothTools()
    {
        string ged = Write("tree.ged", Ged);
        string earlier = Write("earlier.json", TwoCensuses), later = Write("later.json", PlainCensus);
        byte[] before = File.ReadAllBytes(ged);

        var alone = await Validate(ged, later, default);
        var validated = await Validate(ged, later, After(earlier));
        var checkedPlausibility = await CheckPlausibility(ged, later, After(earlier));

        Assert.True(alone.StructuredContent!.Value.GetProperty("success").GetBoolean());
        Assert.Contains("createOrUpdateVital CENS on @I1@: ambiguous — 2 CENS facts match; refine \"match\"", Errors(validated));
        Assert.Contains("createOrUpdateVital CENS on @I1@: ambiguous — 2 CENS facts match; refine \"match\"", Errors(checkedPlausibility));
        Assert.Equal(before, File.ReadAllBytes(ged));
    }

    [Fact]
    public async Task AnAddedCensus_PassesAfterTheEarlierOne()
    {
        string ged = Write("tree.ged", Ged);
        string later = Write("later.json", PlainCensus.Replace("\"fact\": \"CENS\",", "\"fact\": \"CENS\", \"mode\": \"add\","));

        var result = await Validate(ged, later, After(Write("earlier.json", TwoCensuses)));

        var structured = result.StructuredContent!.Value;
        Assert.True(structured.GetProperty("success").GetBoolean(), string.Join("; ", Errors(result)));
        Assert.Contains(structured.GetProperty("log").EnumerateArray(), l => l.GetString()!.StartsWith("after[0] ") && l.GetString()!.EndsWith("(items all): applied in memory"));
    }

    [Fact]
    public async Task AnEarlierChangesetThatDoesNotApply_FailsTheRunNamingIt()
    {
        string ged = Write("tree.ged", Ged);
        string broken = Write("broken.json", PlainCensus.Replace("@I1@", "@I9@"));

        var result = await Validate(ged, Write("later.json", PlainCensus), After(broken));

        Assert.False(result.StructuredContent!.Value.GetProperty("success").GetBoolean());
        Assert.Contains(Errors(result), e => e.StartsWith($"after[0] {broken}: ") && e.Contains("@I9@"));
    }

    [Fact]
    public async Task AnAfterNamingAMissingFile_IsAnError()
    {
        string ged = Write("tree.ged", Ged);
        string later = Write("later.json", PlainCensus);
        string missing = Path.Combine(_dir, "missing.json");

        var validated = await Validate(ged, later, After(missing));
        var checkedPlausibility = await CheckPlausibility(ged, later, After(missing));

        Assert.True(validated.IsError);
        Assert.True(checkedPlausibility.IsError);
        Assert.Contains($"after[0]: Changeset file not found: {missing}", ((TextContentBlock)validated.Content[0]).Text);
    }
}

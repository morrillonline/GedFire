using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class MigrateChangesetToolTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-migrate-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string Write(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    Task<CallToolResult> Migrate(string source, string target, bool overwrite = false) =>
        new MigrateChangesetTool(new ToolGate()).HandleAsync(Json(source), Json(target), Json(overwrite), CancellationToken.None);

    const string Legacy = """
        { "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.",
          "citation": { "source": "@S1@", "page": "p. 1" } } ] } ] }
        """;

    [Fact]
    public async Task ALegacyChangeset_IsWrittenInTheCanonicalForm()
    {
        string target = Path.Combine(_dir, "canonical.json");

        var result = await Migrate(Write("legacy.json", Legacy), target);

        var structured = Structured(result);
        Assert.True(structured.GetProperty("success").GetBoolean());
        Assert.Equal(target, structured.GetProperty("writtenTo").GetString());
        Assert.Contains("items[0].ops[0].citation: now citations, an array",
            structured.GetProperty("changes").EnumerateArray().Select(c => c.GetString()));
        var written = GedCore.Apply.Changeset.Read(File.ReadAllText(target));
        Assert.NotNull(written.Changeset);
        Assert.Empty(written.Warnings);
    }

    [Fact]
    public async Task BothCitationKeys_AreRefusedAndNothingIsWritten()
    {
        string target = Path.Combine(_dir, "canonical.json");
        string both = Legacy.Replace("\"citation\":", "\"citations\": [], \"citation\":");

        var result = await Migrate(Write("both.json", both), target);

        Assert.False(Structured(result).GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Structured(result).GetProperty("writtenTo").ValueKind);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task AnExistingOutPath_IsReplacedOnlyWithOverwrite()
    {
        string source = Write("legacy.json", Legacy);

        var refused = await Migrate(source, source);
        var replaced = await Migrate(source, source, overwrite: true);

        Assert.False(Structured(refused).GetProperty("success").GetBoolean());
        Assert.True(Structured(replaced).GetProperty("success").GetBoolean());
        Assert.Contains("\"citations\"", File.ReadAllText(source));
    }
}

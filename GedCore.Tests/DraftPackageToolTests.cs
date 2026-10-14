using System.Security.Cryptography;
using System.Text.Json;
using GedCore.Apply;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class DraftPackageToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-package-tests-").FullName;
    readonly string _out;

    public DraftPackageToolTests()
    {
        _out = Path.Combine(_dir, "out");
        Directory.CreateDirectory(_out);
    }

    public void Dispose()
    {
        _sessions.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Gladys /Giedd/
        1 SEX F
        0 @I2@ INDI
        1 NAME Hiram /Giedd/
        1 SEX M
        0 @I3@ INDI
        1 NAME Ruth /Giedd/
        1 SEX F
        0 TRLR
        """;

    DraftPackageTool Tool() => new(_sessions.Open(Ged, out string path), path, new ToolGate());

    static string Finding(string id, string anchor, string fact, string date) => $$"""
        { "id": "{{id}}", "correlation": { "kind": "enrich", "target": "{{anchor}}" },
          "draft": { "id": "{{id}}", "extract": "extract of {{id}}",
            "source": { "name": "Find a Grave", "locator": "Memorial #{{id}}", "class": "derivative", "information": "primary" },
            "target": { "xref": "{{anchor}}", "card": "enrich" },
            "facts": [ { "record": "{{anchor}}", "fact": "{{fact}}", "date": "{{date}}", "place": "Rugby", "quay": 2 } ],
            "people": [], "relations": [], "notes": [] },
          "draftGaps": [] }
        """;

    string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    string Cards() => Write("cards.json", """{ "@I1@": [ { "xref": "@I1@", "class": "enrich-person", "gap": "enrich-death", "points": 7 } ] }""");

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    Task<CallToolResult> Call(DraftPackageTool tool, string[] files, string? cards = null, bool overwrite = false, string proposal = "W67-test") =>
        tool.HandleAsync(
            new DraftPackageArguments(Json(files), Json(_out), Json(proposal), cards is null ? default : Json(cards), overwrite ? Json(true) : default),
            CancellationToken.None);

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task EachAnchor_IsWrittenToItsOwnNumberedFileWithTheHashOfItsBytes()
    {
        string a = Write("a.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string b = Write("b.json", Finding("F-2", "@I2@", "BIRT", "1 Jan 1900"));

        var result = await Call(Tool(), [a, b], Cards());

        var structured = Structured(result);
        Assert.True(structured.GetProperty("success").GetBoolean(), structured.GetRawText());
        var items = structured.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([1, 2], items.Select(i => i.GetProperty("item").GetInt32()));
        Assert.Equal(["@I1@", "@I2@"], items.Select(i => i.GetProperty("anchor").GetString()));
        foreach (var item in items)
        {
            string path = item.GetProperty("writtenTo").GetString()!;
            Assert.Equal(Path.Combine(_out, $"W67-test.item{item.GetProperty("item").GetInt32()}.changes.json"), path);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), item.GetProperty("sha256").GetString());
            Assert.Single(Changeset.Parse(File.ReadAllText(path)).Items);
        }
        Assert.Equal(["F-1"], items[0].GetProperty("findingIds").EnumerateArray().Select(i => i.GetString()));
    }

    [Fact]
    public async Task ItemsAreNumberedByTheFirstAppearanceOfEachAnchor()
    {
        string first = Write("1.json", Finding("F-1", "@I2@", "BIRT", "1 Jan 1900"));
        string second = Write("2.json", Finding("F-2", "@I1@", "DEAT", "26 Mar 2013"));
        string third = Write("3.json", Finding("F-3", "@I2@", "DEAT", "2 Feb 1950"));

        var items = Structured(await Call(Tool(), [first, second, third])).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal([("@I2@", 1, 2), ("@I1@", 2, 1)], items.Select(i => (i.GetProperty("anchor").GetString()!, i.GetProperty("item").GetInt32(), i.GetProperty("files").GetArrayLength())));
    }

    [Fact]
    public async Task TheCardsUnderAnAnchor_PriceItsItem()
    {
        string a = Write("a.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string b = Write("b.json", Finding("F-2", "@I2@", "BIRT", "1 Jan 1900"));

        var items = Structured(await Call(Tool(), [a, b], Cards())).GetProperty("items").EnumerateArray().ToList();

        var preview = items[0].GetProperty("pointsPreview");
        Assert.Equal(7, preview.GetProperty("total").GetInt32());
        Assert.Equal("@I1@|enrich-person|enrich-death", Assert.Single(preview.GetProperty("closed").EnumerateArray()).GetProperty("cardKey").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("pointsPreview").ValueKind);
    }

    [Fact]
    public async Task AnAnchorWithAnUnreadyFile_FailsAloneAndTheOthersAreStillWritten()
    {
        string ready = Write("ready.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string gap = Write("gap.json", Finding("F-2", "@I2@", "BIRT", "1 Jan 1900").Replace("\"draftGaps\": []", "\"draftGaps\": [\"needsSex: Mary\"]"));
        string other = Write("other.json", Finding("F-3", "@I3@", "BIRT", "5 May 1910"));

        var structured = Structured(await Call(Tool(), [ready, gap, other]));

        Assert.False(structured.GetProperty("success").GetBoolean());
        var items = structured.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([true, false, true], items.Select(i => i.GetProperty("success").GetBoolean()));
        Assert.Contains("gap.json has draftGaps: needsSex: Mary", items[1].GetProperty("errors")[0].GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("writtenTo").ValueKind);
        Assert.Equal(["W67-test.item1.changes.json", "W67-test.item3.changes.json"], Directory.GetFiles(_out).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task ADraftTheDryRunRejects_IsNotWritten()
    {
        string missingPerson = Write("x.json", Finding("F-1", "@I99@", "DEAT", "26 Mar 2013"));

        var item = Structured(await Call(Tool(), [missingPerson])).GetProperty("items")[0];

        Assert.False(item.GetProperty("success").GetBoolean());
        Assert.NotEmpty(item.GetProperty("errors").EnumerateArray());
        Assert.Empty(Directory.GetFiles(_out));
    }

    [Fact]
    public async Task AFindingThatCompilesToNothing_IsReportedByFileName()
    {
        string empty = Write("empty.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013").Replace(
            "\"facts\": [ { \"record\": \"@I1@\", \"fact\": \"DEAT\", \"date\": \"26 Mar 2013\", \"place\": \"Rugby\", \"quay\": 2 } ]", "\"facts\": []"));

        var item = Structured(await Call(Tool(), [empty])).GetProperty("items")[0];

        Assert.Contains("empty.json (findings[0]) has no facts, relations or notes", item.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task AFileWithNoReadableAnchor_IsAPackageErrorAndBelongsToNoItem()
    {
        string anchored = Write("a.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string stray = Write("stray.json", """{ "id": "F-9" }""");

        var structured = Structured(await Call(Tool(), [anchored, stray]));

        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Single(structured.GetProperty("items").EnumerateArray());
        Assert.Contains("stray.json has no draft", Assert.Single(structured.GetProperty("errors").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task AnExistingFile_IsKeptUnlessOverwriteIsTrue()
    {
        string a = Write("a.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string target = Path.Combine(_out, "W67-test.item1.changes.json");
        File.WriteAllText(target, "keep me");

        var refused = Structured(await Call(Tool(), [a])).GetProperty("items")[0];
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Contains("already exists", refused.GetProperty("errors")[0].GetString());
        Assert.Equal("keep me", File.ReadAllText(target));

        var replaced = Structured(await Call(Tool(), [a], overwrite: true)).GetProperty("items")[0];
        Assert.True(replaced.GetProperty("success").GetBoolean());
        Assert.Contains("createOrUpdateVital", File.ReadAllText(target));
    }

    [Fact]
    public async Task TheSameFilesTwice_GiveTheSameHash()
    {
        string a = Write("a.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        var tool = Tool();

        string first = Structured(await Call(tool, [a])).GetProperty("items")[0].GetProperty("sha256").GetString()!;
        string second = Structured(await Call(tool, [a], overwrite: true)).GetProperty("items")[0].GetProperty("sha256").GetString()!;

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ABadRequest_IsRefusedNamingTheField()
    {
        string a = Write("a.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        var tool = Tool();

        var noFolder = await tool.HandleAsync(new DraftPackageArguments(Json(new[] { a }), default, Json("p"), default, default), CancellationToken.None);
        var missingFolder = await tool.HandleAsync(new DraftPackageArguments(Json(new[] { a }), Json(Path.Combine(_dir, "nope")), Json("p"), default, default), CancellationToken.None);
        var badName = await Call(tool, [a], proposal: "a/b");
        var noPaths = await tool.HandleAsync(new DraftPackageArguments(default, Json(_out), Json("p"), default, default), CancellationToken.None);
        var missingCards = await Call(tool, [a], cards: Path.Combine(_dir, "no-cards.json"));

        Assert.All(new[] { noFolder, missingFolder, badName, noPaths, missingCards }, r => Assert.True(r.IsError));
        Assert.Contains("folder is required", Text(noFolder));
        Assert.Contains("does not exist", Text(missingFolder));
        Assert.Contains("proposal", Text(badName));
        Assert.Contains("findingPaths is required", Text(noPaths));
        Assert.Contains("cardsPath", Text(missingCards));
        Assert.Empty(Directory.GetFiles(_out));
    }

    [Fact]
    public void TheOutputSchema_NamesTheFieldsTheResultCarries()
    {
        using var schema = JsonDocument.Parse(DraftPackageTool.OutputSchemaJson);

        var item = schema.RootElement.GetProperty("properties").GetProperty("items").GetProperty("items");
        string[] required = [.. item.GetProperty("required").EnumerateArray().Select(r => r.GetString()!)];
        Assert.Contains("sha256", required);
        Assert.Contains("pointsPreview", required);
        Assert.Contains("plausibility", required);
        Assert.True(item.GetProperty("properties").GetProperty("pointsPreview").TryGetProperty("anyOf", out _));
    }
}

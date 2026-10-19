using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Ged70;
using GedCore.Policy;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

/// <summary>
/// The reportPath of apply_changeset, validate_changeset and draft_changeset: the full result is written to the file,
/// exactly as a verbose call returns it, and the default response is a digest of that report.
/// </summary>
public class ChangesetReportTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-report-tests-").FullName;

    public void Dispose()
    {
        _sessions.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    const string Changeset = """
        { "newSources": [ { "xref": "@NewSource1@", "ops": [
            { "op": "createOrUpdateSource", "xref": "@NewSource1@", "title": "A register" } ] } ],
          "items": [ { "item": 1, "ops": [
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
              "value": { "date": "1 JAN 1900", "place": "Salem" },
              "citation": { "source": "@NewSource1@", "page": "p. 1", "quay": 2 } } ] } ] }
        """;

    string WriteGed()
    {
        string path = Path.Combine(_dir, "test.ged");
        var document = Ged70Parser.Parse(string.Join("\r\n",
        [
            "0 HEAD", "1 GEDC", "2 VERS 7.0",
            "0 @I00001@ INDI", "1 NAME Allen /Test/", "1 SEX M",
            "0 TRLR",
        ]) + "\r\n");
        using var stream = File.Create(path);
        Ged70Formatter.Write(document, stream);
        return path;
    }

    string WriteChangeset(string json = Changeset)
    {
        string path = Path.Combine(_dir, "changeset.json");
        File.WriteAllText(path, json);
        return path;
    }

    string Report(string name = "report.json") => Path.Combine(_dir, name);

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    static void AssertReportIsTheResult(string reportPath, CallToolResult result)
    {
        var report = JsonNode.Parse(File.ReadAllText(reportPath));
        var returned = JsonNode.Parse(Structured(result).GetRawText());
        Assert.True(JsonNode.DeepEquals(report, returned), "the report is not the result the tool returned");
    }

    static JsonNode ReadReport(string reportPath) => JsonNode.Parse(File.ReadAllText(reportPath))!;

    // ---- validate_changeset ----

    [Fact]
    public async Task Validate_WithAReportPath_ReturnsADigestOfTheFullReport()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string report = Report();

        var result = await tool.HandleAsync(new ValidateChangesetArguments(WriteChangeset(), "all", ReportPath: Json(report)), CancellationToken.None);

        var digest = Structured(result);
        Assert.True(digest.GetProperty("success").GetBoolean());
        Assert.Equal(report, digest.GetProperty("reportWrittenTo").GetString());
        Assert.False(digest.TryGetProperty("log", out _));
        var file = ReadReport(report);
        Assert.Equal(file["success"]!.GetValue<bool>(), digest.GetProperty("success").GetBoolean());
        Assert.Equal(file["errors"]!.AsArray().Count, digest.GetProperty("errorCount").GetInt32());
        Assert.Equal(file["log"]!.AsArray().Count, digest.GetProperty("logCount").GetInt32());
        Assert.Equal(1, Assert.Single(digest.GetProperty("items").EnumerateArray()).GetInt32());
        Assert.NotNull(file["log"]);
    }

    [Fact]
    public async Task Validate_Verbose_ReturnsTheExactResultWrittenToTheReport()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string report = Report();

        var result = await tool.HandleAsync(
            new ValidateChangesetArguments(WriteChangeset(), "all", ReportPath: Json(report), Verbose: Json(true)), CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
        Assert.Equal(report, Structured(result).GetProperty("reportWrittenTo").GetString());
        AssertReportIsTheResult(report, result);
    }

    [Fact]
    public async Task Validate_WithoutAReportPath_WritesNothingAndSaysSo()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(), "all", CancellationToken.None);

        Assert.Equal(JsonValueKind.Null, Structured(result).GetProperty("reportWrittenTo").ValueKind);
        Assert.Empty(Directory.GetFiles(_dir, "report*"));
    }

    [Fact]
    public async Task Validate_AFailedValidationIsReportedToo()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string report = Report();
        string bad = """{ "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateNote", "record": "@I09999@", "text": "x" } ] } ] }""";

        var result = await tool.HandleAsync(new ValidateChangesetArguments(WriteChangeset(bad), "all", ReportPath: Json(report)), CancellationToken.None);

        var digest = Structured(result);
        Assert.False(digest.GetProperty("success").GetBoolean());
        Assert.True(digest.GetProperty("errorCount").GetInt32() > 0);
        var file = ReadReport(report);
        Assert.False(file["success"]!.GetValue<bool>());
        Assert.Equal(file["errors"]!.AsArray().Count, digest.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Validate_AnExistingReport_IsRefusedUntouchedUnlessOverwriteIsTrue()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string report = Report();
        File.WriteAllText(report, "keep me");

        var refused = await tool.HandleAsync(new ValidateChangesetArguments(WriteChangeset(), "all", ReportPath: Json(report)), CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Contains("already exists", Text(refused));
        Assert.Equal("keep me", File.ReadAllText(report));

        var replaced = await tool.HandleAsync(
            new ValidateChangesetArguments(WriteChangeset(), "all", ReportPath: Json(report), Overwrite: Json(true)), CancellationToken.None);
        Assert.True(Structured(replaced).GetProperty("success").GetBoolean());
        Assert.True(ReadReport(report)["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Validate_WithCardsAndAReportPath_DigestCarriesThePointsTotal()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string report = Report();
        var cards = Json(new[] { new { xref = "@I00001@", @class = "enrich-person", gap = "enrich-death", points = 7 } });

        var result = await tool.HandleAsync(
            new ValidateChangesetArguments(WriteChangeset(), "all", Cards: cards, ReportPath: Json(report)), CancellationToken.None);

        var digest = Structured(result);
        Assert.False(digest.TryGetProperty("pointsPreview", out _));
        Assert.Equal(
            ReadReport(report)["pointsPreview"]!["total"]!.GetValue<int>(),
            digest.GetProperty("pointsPreviewTotal").GetInt32());
    }

    [Theory]
    [InlineData("missing-folder/report.json", "its folder does not exist")]
    [InlineData("   ", "reportPath must not be blank")]
    public async Task Validate_AnUnusableReportPath_IsRefusedBeforeAnyWork(string relative, string expected)
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string path = relative.Trim().Length == 0 ? relative : Path.Combine(_dir, relative);

        var result = await tool.HandleAsync(new ValidateChangesetArguments(WriteChangeset(), "all", ReportPath: Json(path)), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
    }

    [Fact]
    public async Task Validate_AReportPathThatIsNotAString_IsRefusedNamingTheField()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(new ValidateChangesetArguments(WriteChangeset(), "all", ReportPath: Json(5)), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("reportPath must be a string", Text(result));
    }

    // ---- apply_changeset ----

    [Fact]
    public async Task Apply_ReportsTheMintedXrefsAndStillWritesTheDocument()
    {
        string ged = WriteGed();
        var tool = new ApplyChangesetTool(ged, new ToolGate(), readOnly: false);
        string report = Report();

        var result = await tool.HandleAsync(WriteChangeset(), "all", Json(report), default, CancellationToken.None);

        var digest = Structured(result);
        Assert.True(digest.GetProperty("success").GetBoolean());
        Assert.Equal(1, digest.GetProperty("mintedXrefCount").GetInt32());
        Assert.Equal(1, digest.GetProperty("deltas").GetProperty("SOUR").GetInt32());
        var minted = ReadReport(report)["mintedXrefs"]!.AsObject();
        Assert.Equal("@NewSource1@", Assert.Single(minted).Key);
        Assert.Contains("A register", File.ReadAllText(ged));
    }

    [Fact]
    public async Task Apply_Verbose_ReturnsTheExactResultWrittenToTheReport()
    {
        string ged = WriteGed();
        var tool = new ApplyChangesetTool(ged, new ToolGate(), readOnly: false);
        string report = Report();

        var result = await tool.HandleAsync(WriteChangeset(), "all", Json(report), default, Json(true), CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
        Assert.Equal("@NewSource1@", Structured(result).GetProperty("mintedXrefs").EnumerateObject().Single().Name);
        AssertReportIsTheResult(report, result);
    }

    [Fact]
    public async Task Apply_AnExistingReport_IsRefusedBeforeTheDocumentIsTouched()
    {
        string ged = WriteGed();
        byte[] before = File.ReadAllBytes(ged);
        var tool = new ApplyChangesetTool(ged, new ToolGate(), readOnly: false);
        string report = Report();
        File.WriteAllText(report, "keep me");

        var result = await tool.HandleAsync(WriteChangeset(), "all", Json(report), default, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(before, File.ReadAllBytes(ged));
        Assert.Equal("keep me", File.ReadAllText(report));
    }

    [Fact]
    public async Task Apply_ARefusedPolicyViolationIsReportedToo()
    {
        string ged = WriteGed();
        byte[] before = File.ReadAllBytes(ged);
        var policy = ChangesetPolicy.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{ "bannedPhrases": ["A register"] }""")));
        var tool = new ApplyChangesetTool(ged, new ToolGate(), readOnly: false, policy);
        string report = Report();
        string withNote = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "text": "A register says so." } ] } ] }
            """;

        var result = await tool.HandleAsync(WriteChangeset(withNote), "all", Json(report), default, CancellationToken.None);

        var digest = Structured(result);
        Assert.False(digest.GetProperty("success").GetBoolean());
        Assert.True(digest.GetProperty("errorCount").GetInt32() > 0);
        Assert.False(ReadReport(report)["success"]!.GetValue<bool>());
        Assert.Equal(before, File.ReadAllBytes(ged));
    }

    [Fact]
    public async Task Apply_OnAReadOnlyServer_IsRefusedAndWritesNoReport()
    {
        var tool = new ApplyChangesetTool(WriteGed(), new ToolGate(), readOnly: true);
        string report = Report();

        var result = await tool.HandleAsync(WriteChangeset(), "all", Json(report), default, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.False(File.Exists(report));
    }

    // ---- draft_changeset ----

    const string Findings = """
        [ { "id": "F-1", "extract": "DEATH 26 Mar 2013",
            "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
            "target": { "xref": "@I00001@", "card": "enrich-death" },
            "facts": [ { "record": "@I00001@", "fact": "DEAT", "date": "26 Mar 2013", "place": "Rugby", "quay": 2 } ] } ]
        """;

    DraftChangesetTool Draft() => new(_sessions.Open(File.ReadAllText(WriteGed())), new ToolGate());

    static JsonElement Findings_() => JsonDocument.Parse(Findings).RootElement.Clone();

    [Fact]
    public async Task Draft_WithAReportPath_ReturnsADigestOfTheFullReport()
    {
        string report = Report();

        var result = await Draft().HandleAsync(new DraftChangesetArguments(Findings_(), default, default, default, default, Json(report)), CancellationToken.None);

        var digest = Structured(result);
        Assert.True(digest.GetProperty("success").GetBoolean());
        Assert.Equal(report, digest.GetProperty("reportWrittenTo").GetString());
        Assert.False(digest.TryGetProperty("changeset", out _));
        Assert.False(digest.TryGetProperty("decisions", out _));
        var file = ReadReport(report);
        Assert.NotNull(file["changeset"]);
        var item = Assert.Single(digest.GetProperty("items").EnumerateArray());
        var fileItem = Assert.Single(file["changeset"]!["items"]!.AsArray());
        Assert.Equal(fileItem!["item"]!.GetValue<int>(), item.GetProperty("item").GetInt32());
        Assert.Equal(fileItem["target"]!.GetValue<string>(), item.GetProperty("target").GetString());
        Assert.Equal(fileItem["ops"]!.AsArray().Count, item.GetProperty("ops").GetInt32());
        Assert.Contains($"{file["decisions"]!.AsArray().Count} decisions", digest.GetProperty("decisionsSummary").GetString());
    }

    [Fact]
    public async Task Draft_Verbose_ReturnsTheExactResultWrittenToTheReport()
    {
        string report = Report();

        var result = await Draft().HandleAsync(
            new DraftChangesetArguments(Findings_(), default, default, default, default, Json(report), Json(true)), CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
        Assert.Equal(report, Structured(result).GetProperty("reportWrittenTo").GetString());
        AssertReportIsTheResult(report, result);
        Assert.NotNull(ReadReport(report)["changeset"]);
    }

    [Fact]
    public async Task Draft_ATenOpItemWithAReportPath_ReturnsUnderAThousandCharacters()
    {
        string report = Report();
        string facts = string.Join(", ", Enumerable.Range(1900, 10).Select(year =>
            $$"""{ "record": "@I00001@", "fact": "RESI", "date": "{{year}}", "place": "Rugby", "quay": 2 }"""));
        string findings = Findings.Replace(
            """[ { "record": "@I00001@", "fact": "DEAT", "date": "26 Mar 2013", "place": "Rugby", "quay": 2 } ]""",
            $"[ {facts} ]");

        var result = await Draft().HandleAsync(
            new DraftChangesetArguments(JsonDocument.Parse(findings).RootElement.Clone(), default, default, default, default, Json(report)),
            CancellationToken.None);

        var digest = Structured(result);
        Assert.True(digest.GetProperty("success").GetBoolean(), digest.GetRawText());
        Assert.True(digest.GetRawText().Length < 1000, $"digest is {digest.GetRawText().Length} characters");
        var item = Assert.Single(digest.GetProperty("items").EnumerateArray());
        Assert.Equal(
            Assert.Single(ReadReport(report)["changeset"]!["items"]!.AsArray())!["ops"]!.AsArray().Count,
            item.GetProperty("ops").GetInt32());
    }

    [Fact]
    public async Task Draft_AFailedDraftIsReportedToo()
    {
        string report = Report();
        string tooHigh = Findings.Replace("\"quay\": 2", "\"quay\": 3");

        var result = await Draft().HandleAsync(
            new DraftChangesetArguments(JsonDocument.Parse(tooHigh).RootElement.Clone(), default, default, default, default, Json(report)), CancellationToken.None);

        var digest = Structured(result);
        Assert.False(digest.GetProperty("success").GetBoolean());
        Assert.NotEmpty(digest.GetProperty("errors").EnumerateArray());
        Assert.False(ReadReport(report)["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Draft_AReportAndAChangesetFile_AreBothWritten()
    {
        string report = Report();
        string changeset = Report("changeset.out.json");

        var result = await Draft().HandleAsync(
            new DraftChangesetArguments(Findings_(), default, default, Json(changeset), default, Json(report)), CancellationToken.None);

        Assert.True(File.Exists(changeset));
        Assert.Equal(changeset, Structured(result).GetProperty("writtenTo").GetString());
        Assert.Equal(changeset, ReadReport(report)["writtenTo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Draft_TheReportCannotBeTheChangesetFile()
    {
        string path = Report();

        var result = await Draft().HandleAsync(
            new DraftChangesetArguments(Findings_(), default, default, Json(path), default, Json(path)), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("must differ from outPath", Text(result));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Draft_AnExistingReport_IsRefusedUnlessOverwriteIsTrue()
    {
        string report = Report();
        File.WriteAllText(report, "keep me");

        var refused = await Draft().HandleAsync(new DraftChangesetArguments(Findings_(), default, default, default, default, Json(report)), CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Equal("keep me", File.ReadAllText(report));

        var replaced = await Draft().HandleAsync(new DraftChangesetArguments(Findings_(), default, default, default, Json(true), Json(report)), CancellationToken.None);
        Assert.Equal(report, Structured(replaced).GetProperty("reportWrittenTo").GetString());
        Assert.True(ReadReport(report)["success"]!.GetValue<bool>());
    }

    // ---- advertised schemas ----

    [Fact]
    public void TheThreeToolsAdvertiseReportPathAndTheResultsNameTheReport()
    {
        foreach (string input in new[] { ApplyChangesetTool.InputSchemaJson, ValidateChangesetTool.InputSchemaJson, DraftChangesetTool.InputSchemaJson })
        {
            Assert.Contains("\"reportPath\"", input);
            Assert.Contains("\"verbose\"", input);
        }
        Assert.DoesNotContain("\"reportPath\"", CheckPlausibilityTool.InputSchemaJson);
        foreach (string output in new[] { ApplyChangesetTool.OutputSchemaJson, ValidateChangesetTool.OutputSchemaJson, DraftChangesetTool.OutputSchemaJson })
            Assert.Contains("\"reportWrittenTo\"", output);
    }
}

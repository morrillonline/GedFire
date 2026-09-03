using System.Text.Json;

namespace GedCore.Tests;

// Subprocess integration tests: launch the packed `gedfire mcp` command
// against a synthetic GEDCOM and drive it over real stdio, the way an
// actual MCP client would.
public class McpServerIntegrationTests : IDisposable
{
    static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(15);

    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-mcp-integration-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteGed(string personName = "Frederick /Morrill/", string sex = "M")
    {
        string path = Path.Combine(_dir, "test.ged");
        File.WriteAllText(path, $"""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME {personName}
            1 SEX {sex}
            1 BIRT
            2 DATE 12 MAR 1841
            2 PLAC Gorham, Maine
            0 TRLR

            """);
        return path;
    }

    // -------------------------------------------------------------------
    // Startup errors
    // -------------------------------------------------------------------

    [Fact]
    public async Task MissingInputArgument_Exits1WithEmptyStdout()
    {
        var (exitCode, stdout, stderr) = await McpStdioTestClient.RunToCompletionAsync(ShortTimeout, "mcp");

        Assert.Equal(1, exitCode);
        Assert.Equal("", stdout);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public async Task MissingInputFile_Exits1WithEmptyStdout()
    {
        string missing = Path.Combine(_dir, "does-not-exist.ged");
        var (exitCode, stdout, stderr) = await McpStdioTestClient.RunToCompletionAsync(
            ShortTimeout, "mcp", "--input", missing);

        Assert.Equal(1, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains(missing, stderr);
    }

    [Fact]
    public async Task UnparsableInputFile_Exits1WithEmptyStdout()
    {
        string path = Path.Combine(_dir, "garbage.ged");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00]);

        var (exitCode, stdout, _) = await McpStdioTestClient.RunToCompletionAsync(
            ShortTimeout, "mcp", "--input", path);

        Assert.Equal(1, exitCode);
        Assert.Equal("", stdout);
    }

    // -------------------------------------------------------------------
    // Protocol handshake and tool listing
    // -------------------------------------------------------------------

    [Fact]
    public async Task Initialize_ReturnsServerInfoAndDocumentScopeInstructions()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());

        var response = await client.SendRequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "0.0.1" },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.Equal("gedfire", result.GetProperty("serverInfo").GetProperty("name").GetString());
        string instructions = result.GetProperty("instructions").GetString()!;
        Assert.Contains("belongs only to", instructions);
        Assert.False(response.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ToolsList_AdvertisesAllToolsWithTruthfulReadOnlyAnnotations()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/list", null, ShortTimeout);
        var tools = response.GetProperty("result").GetProperty("tools");
        Assert.Equal(8, tools.GetArrayLength());

        // The SDK's own McpServerPrimitiveCollection does not preserve the
        // alphabetical order this server registers tools in — confirmed
        // empirically, not assumed. Order within one process is covered
        // separately by ToolsList_IsDeterministicAcrossCalls; here only
        // membership is asserted.
        Assert.Equal(
            new HashSet<string> {
                "apply_changeset", "check_plausibility", "date_calc", "describe_changeset_ops", "find_person",
                "get_document_stats", "get_record", "validate_changeset",
            },
            tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToHashSet());

        // apply_changeset is the one tool on this server that writes to the
        // file, so it alone carries the opposite annotations; every other
        // tool — including validate_changeset, its dry-run twin — is
        // read-only, non-destructive, and idempotent.
        foreach (var tool in tools.EnumerateArray())
        {
            bool isApply = tool.GetProperty("name").GetString() == "apply_changeset";
            var annotations = tool.GetProperty("annotations");
            Assert.Equal(!isApply, annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.Equal(isApply, annotations.GetProperty("destructiveHint").GetBoolean());
            Assert.Equal(!isApply, annotations.GetProperty("idempotentHint").GetBoolean());
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
            Assert.True(tool.TryGetProperty("outputSchema", out _));
        }

        var dateCalc = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "date_calc");
        Assert.Contains("without reading", dateCalc.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ToolsList_AdvertisesGetDocumentStatsWithEmptyInputSchema()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/list", null, ShortTimeout);
        var tools = response.GetProperty("result").GetProperty("tools");
        var tool = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "get_document_stats");

        var annotations = tool.GetProperty("annotations");
        Assert.True(annotations.GetProperty("readOnlyHint").GetBoolean());
        Assert.False(annotations.GetProperty("destructiveHint").GetBoolean());
        Assert.True(annotations.GetProperty("idempotentHint").GetBoolean());

        var inputSchema = tool.GetProperty("inputSchema");
        Assert.Equal("object", inputSchema.GetProperty("type").GetString());
        Assert.Empty(inputSchema.GetProperty("properties").EnumerateObject());
        Assert.True(tool.TryGetProperty("outputSchema", out _));
    }

    [Fact]
    public async Task ToolsList_IsDeterministicAcrossCalls()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var first = await client.SendRequestAsync("tools/list", null, ShortTimeout);
        var second = await client.SendRequestAsync("tools/list", null, ShortTimeout);

        Assert.Equal(
            JsonSerializer.Serialize(first.GetProperty("result")),
            JsonSerializer.Serialize(second.GetProperty("result")));
    }

    // -------------------------------------------------------------------
    // tools/call
    // -------------------------------------------------------------------

    [Fact]
    public async Task ToolsCall_DateCalc_ReturnsCanonicalStructuredResult()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "date_calc",
            arguments = new
            {
                operation = "diff",
                from = "27 SEP 1777",
                to = "29 JAN 1841",
            },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.Equal("diff", structured.GetProperty("operation").GetString());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("date").ValueKind);
        Assert.Equal("63y 4m 2d", structured.GetProperty("age").GetString());
    }

    [Fact]
    public async Task ToolsCall_Success_UsesStructuredContentAndTextFallback()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Frederick Morrill" },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isErr) && isErr.GetBoolean());

        var structured = result.GetProperty("structuredContent");
        Assert.Equal("single", structured.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", structured.GetProperty("person").GetProperty("xref").GetString());

        var content = result.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        using var textAsJson = JsonDocument.Parse(content[0].GetProperty("text").GetString()!);
        Assert.Equal(
            JsonSerializer.Serialize(structured),
            JsonSerializer.Serialize(textAsJson.RootElement));
    }

    [Fact]
    public async Task ToolsCall_BlankQuery_ReturnsIsErrorNotAProtocolFailure()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "   " },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.False(response.TryGetProperty("error", out _)); // still a JSON-RPC success envelope
    }

    [Fact]
    public async Task ToolsCall_WithHints_NarrowsToTheHintedCandidate()
    {
        string path = Path.Combine(_dir, "two.ged");
        File.WriteAllText(path, """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 12 MAR 1841
            0 @I2@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 12 MAR 1900
            0 TRLR

            """);

        await using var client = McpStdioTestClient.Start(path);
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Frederick Morrill", hints = new { birth = new { year = 1841 } } },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("single", structured.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", structured.GetProperty("person").GetProperty("xref").GetString());
    }

    [Fact]
    public async Task ToolsCall_WithLegacyFlatHint_ReturnsIsError()
    {
        string path = WriteGed();
        await using var client = McpStdioTestClient.Start(path);
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Frederick Morrill", hints = new { birthYear = 1841 } },
        }, ShortTimeout);

        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    // -------------------------------------------------------------------
    // maxResults, over real stdio JSON-RPC: proves the wire-level integer
    // binds through the SDK's argument binder, not just
    // FindPersonTool.HandleAsync called in-process.
    // -------------------------------------------------------------------

    string WriteTenJaneDoesGed()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("0 HEAD");
        sb.AppendLine("1 GEDC");
        sb.AppendLine("2 VERS 7.0");
        for (int i = 1; i <= 10; i++)
        {
            sb.AppendLine($"0 @I{i:D2}@ INDI");
            sb.AppendLine("1 NAME Jane /Doe/");
            sb.AppendLine("1 SEX F");
        }
        sb.AppendLine("0 TRLR");
        string path = Path.Combine(_dir, "ten.ged");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    [Fact]
    public async Task ToolsCall_MaxResults_Integer_CapsCandidatesOverRealJsonRpc()
    {
        await using var client = McpStdioTestClient.Start(WriteTenJaneDoesGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Jane Doe", maxResults = 3 },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("candidates", structured.GetProperty("matchType").GetString());
        Assert.Equal(3, structured.GetProperty("candidates").GetArrayLength());
        Assert.Equal(10, structured.GetProperty("totalMatches").GetInt32());
        Assert.True(structured.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ToolsCall_MaxResultsTwenty_IsAcceptedOverRealJsonRpc()
    {
        await using var client = McpStdioTestClient.Start(WriteTenJaneDoesGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Jane Doe", maxResults = 20 },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal(10, structured.GetProperty("candidates").GetArrayLength());
        Assert.Equal(10, structured.GetProperty("totalMatches").GetInt32());
        Assert.False(structured.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("\"all\"")]
    public async Task ToolsCall_MaxResultsInvalid_ReturnsIsErrorOverRealJsonRpc(string maxResultsJson)
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        using var arguments = JsonDocument.Parse($$"""{"query":"Frederick Morrill","maxResults":{{maxResultsJson}}}""");
        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = arguments.RootElement,
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task ToolsCall_GetDocumentStats_ReturnsCountsAndVersionForNoArguments()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "get_document_stats",
            arguments = new { },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isErr) && isErr.GetBoolean());

        var structured = result.GetProperty("structuredContent");
        Assert.Equal(1, structured.GetProperty("personCount").GetInt32());
        Assert.Equal(0, structured.GetProperty("familyCount").GetInt32());
        Assert.Equal("7.0", structured.GetProperty("gedVersion").GetString());
    }

    [Fact]
    public async Task ToolsCall_GetRecord_ResolvesAPersonXref()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "get_record",
            arguments = new { xref = "@I1@" },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isErr) && isErr.GetBoolean());

        var structured = result.GetProperty("structuredContent");
        Assert.Equal("person", structured.GetProperty("recordType").GetString());
        Assert.Equal("@I1@", structured.GetProperty("xref").GetString());
        Assert.Equal("Frederick Morrill", structured.GetProperty("name").GetString());
        Assert.Equal("12 MAR 1841", structured.GetProperty("birth").GetProperty("date").GetString());
    }

    [Fact]
    public async Task ToolsCall_GetRecord_UnresolvableXref_ReturnsNotFound()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "get_record",
            arguments = new { xref = "@I99999@" },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("not_found", structured.GetProperty("recordType").GetString());
    }

    [Fact]
    public async Task ToolsCall_GetRecord_DefaultMediaDir_ResolvesASelfDescribingPathUnderTheGedcomDir()
    {
        // No --media-dir given: the default is the GEDCOM's own directory,
        // matching generate. A self-describing "media/..." payload -- the
        // shape MediaFileRequest.NormalizePath always produces -- resolves
        // correctly against that plain default with no special-casing.
        string mediaSubdir = Path.Combine(_dir, "media");
        Directory.CreateDirectory(mediaSubdir);
        File.WriteAllText(Path.Combine(mediaSubdir, "portrait.jpg"), "fake-jpeg-bytes");

        string path = Path.Combine(_dir, "test.ged");
        File.WriteAllText(path, """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @M1@ OBJE
            1 FILE media/portrait.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 OBJE @M1@
            0 TRLR

            """);

        await using var client = McpStdioTestClient.Start(path);
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "get_record",
            arguments = new { xref = "@I1@" },
        }, ShortTimeout);

        var file = response.GetProperty("result").GetProperty("structuredContent")
            .GetProperty("media")[0].GetProperty("files")[0];
        Assert.True(file.GetProperty("resolved").GetBoolean());
        Assert.Equal(Path.Combine(mediaSubdir, "portrait.jpg"), file.GetProperty("path").GetString());
    }

    // -------------------------------------------------------------------
    // Transport discipline and lifecycle
    // -------------------------------------------------------------------

    [Fact]
    public async Task EveryStdoutLine_IsValidJsonRpc_NeverContaminatedByDiagnostics()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());

        var initResponse = await client.SendRequestAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "test", version = "0.0.1" },
        }, ShortTimeout);
        await client.SendNotificationAsync("notifications/initialized");
        var listResponse = await client.SendRequestAsync("tools/list", null, ShortTimeout);
        var callResponse = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Frederick Morrill" },
        }, ShortTimeout);

        foreach (var response in new[] { initResponse, listResponse, callResponse })
        {
            Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
            Assert.True(response.TryGetProperty("result", out _));
        }
    }

    [Fact]
    public async Task ClosingStdin_ExitsPromptlyWithCodeZero()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);
        await client.SendRequestAsync("tools/list", null, ShortTimeout);

        int exitCode = await client.CloseStdinAndWaitForExitAsync(ShortTimeout);

        Assert.Equal(0, exitCode);
    }

    // -------------------------------------------------------------------
    // Resident document reload
    // -------------------------------------------------------------------

    [Fact]
    public async Task DocumentChangedOnDisk_ReloadsAndReflectsTheChangeOnTheNextCall()
    {
        string path = WriteGed("Frederick /Morrill/");
        await using var client = McpStdioTestClient.Start(path);
        await client.InitializeAsync(ShortTimeout);

        var before = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Gwendolyn Ashworth" },
        }, ShortTimeout);
        Assert.Equal("none", before.GetProperty("result").GetProperty("structuredContent").GetProperty("matchType").GetString());

        // Replace the file with a version containing the person we just
        // failed to find, with a distinguishable mtime.
        File.WriteAllText(path, """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Gwendolyn /Ashworth/
            1 SEX F
            0 TRLR

            """);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var after = await client.SendRequestAsync("tools/call", new
        {
            name = "find_person",
            arguments = new { query = "Gwendolyn Ashworth" },
        }, ShortTimeout);

        var structured = after.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("single", structured.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", structured.GetProperty("person").GetProperty("xref").GetString());
    }

    // -------------------------------------------------------------------
    // validate_changeset / apply_changeset
    // -------------------------------------------------------------------

    string WriteChangeset(string json)
    {
        string path = Path.Combine(_dir, "changeset.json");
        File.WriteAllText(path, json);
        return path;
    }

    const string AddNoteToI1Changeset = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateNote", "record": "@I1@", "text": "A note." } ] } ] }
        """;

    [Fact]
    public async Task ToolsCall_DescribeChangesetOps_ReturnsTheDialectCatalog()
    {
        await using var client = McpStdioTestClient.Start(WriteGed());
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "describe_changeset_ops",
            arguments = new { },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.True(structured.GetProperty("envelope").TryGetProperty("example", out _));
        var ops = structured.GetProperty("ops");
        Assert.True(ops.GetArrayLength() >= 17);
        Assert.Contains(
            ops.EnumerateArray().Select(o => o.GetProperty("op").GetString()),
            name => name == "createOrUpdateVital");
    }

    [Fact]
    public async Task ToolsCall_ValidateChangeset_SucceedsAndLeavesTheFileUntouched()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(AddNoteToI1Changeset);
        await using var client = McpStdioTestClient.Start(gedPath);
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "validate_changeset",
            arguments = new { changesetPath, items = "all" },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.True(structured.GetProperty("success").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public async Task ToolsCall_ApplyChangeset_WritesTheBoundFile()
    {
        string gedPath = WriteGed();
        string changesetPath = WriteChangeset(AddNoteToI1Changeset);
        await using var client = McpStdioTestClient.Start(gedPath);
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "apply_changeset",
            arguments = new { changesetPath, items = "all" },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.True(structured.GetProperty("success").GetBoolean());
        Assert.Contains("A note.", File.ReadAllText(gedPath));
    }

    [Fact]
    public async Task ToolsCall_ApplyChangeset_OnReadOnlyServer_RefusesAndLeavesTheFileUntouched()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(AddNoteToI1Changeset);
        await using var client = McpStdioTestClient.Start(gedPath, "--read-only");
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "apply_changeset",
            arguments = new { changesetPath, items = "all" },
        }, ShortTimeout);

        var result = response.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("--read-only", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public async Task ToolsCall_ValidateChangeset_OnReadOnlyServer_StillWorks()
    {
        // --read-only disables the write path only; the dry-run preview
        // tool stays available so an agent can still check a changeset.
        string gedPath = WriteGed();
        string changesetPath = WriteChangeset(AddNoteToI1Changeset);
        await using var client = McpStdioTestClient.Start(gedPath, "--read-only");
        await client.InitializeAsync(ShortTimeout);

        var response = await client.SendRequestAsync("tools/call", new
        {
            name = "validate_changeset",
            arguments = new { changesetPath, items = "all" },
        }, ShortTimeout);

        var structured = response.GetProperty("result").GetProperty("structuredContent");
        Assert.True(structured.GetProperty("success").GetBoolean());
    }
}

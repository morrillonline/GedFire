using System.Text.Json;
using GedFire.Cli;
using GedFire.Mcp;

namespace GedCore.Tests;

public class ToolsCommandTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _directory = Directory.CreateTempSubdirectory("gedfire-tools-cli-tests-").FullName;

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Mara /Example/
        1 SEX F
        1 BIRT
        2 DATE 1800
        1 DEAT
        2 DATE 1870
        0 TRLR
        """;

    const string Changeset = """
        {"items":[{"item":1,"ops":[{"op":"createOrUpdateNote","record":"@I1@","text":"Synthetic evidence."}]}]}
        """;

    public void Dispose()
    {
        _sessions.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    string Document(string text = Ged)
    {
        _sessions.Open(text, out string path);
        return path;
    }

    string JsonFile(string name, string json)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    static async Task<(int Exit, string Output, string Error)> Run(string[] args, string input = "{}")
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit = await CliApplication.CreateDefault(new CommandContext(output, error, new StringReader(input)))
            .RunAsync(["tools", .. args]);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task List_ContainsEveryRegisteredToolWithoutOpeningADocument()
    {
        var result = await Run(["list"]);

        Assert.Equal(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(McpToolCollectionFactory.Describe().Select(t => t.Name),
            json.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task Describe_ReturnsTheMcpSchema()
    {
        var result = await Run(["describe", "date_calc"]);

        Assert.Equal(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        using var expected = JsonDocument.Parse(DateCalcTool.InputSchemaJson);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, json.RootElement.GetProperty("inputSchema")));
    }

    [Fact]
    public async Task Call_ReadsStdinAndReturnsTheHandlersStructuredResult()
    {
        var result = await Run(["call", "date_calc", "--args", "-"], """{"operation":"normalize","date":"1 JAN 1800"}""");
        var expected = await new DateCalcTool(new ToolGate()).HandleAsync("normalize", "1 JAN 1800", null, null, null, CancellationToken.None);

        Assert.Equal(0, result.Exit);
        Assert.Equal(JsonSerializer.Serialize(expected.StructuredContent), result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"operation\":\"normalize\",\"operation\":\"diff\"}")]
    public async Task Call_InvalidArgumentsFail(string input)
    {
        var result = await Run(["call", "date_calc", "--args", "-"], input);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task Call_SchemaErrorsAreJsonWithANonzeroExitCode()
    {
        var result = await Run(["call", "date_calc", "--args", "-"], """{"bogus":true}""");

        Assert.Equal(1, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("isError").GetBoolean());
        Assert.Contains("missing required parameter", result.Error);
        Assert.Contains("unrecognized parameter", result.Error);
    }

    [Fact]
    public async Task Call_DocumentToolRequiresInput()
    {
        var result = await Run(["call", "get_records"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("requires --input", result.Error);
    }

    [Fact]
    public async Task UnknownToolIsReported()
    {
        var result = await Run(["call", "unknown"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("Unknown tool", result.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpSucceeds(string flag)
    {
        var result = await Run([flag]);

        Assert.Equal(0, result.Exit);
        Assert.Contains("Usage: gedfire tools", result.Output);
    }

    [Theory]
    [InlineData("call", "date_calc", "--bogus")]
    [InlineData("describe", "date_calc", "--bogus")]
    [InlineData("list", "--bogus")]
    public async Task InvalidCommandOptionsFail(params string[] args)
    {
        var result = await Run(args);

        Assert.Equal(1, result.Exit);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task Call_ReadsArgumentsFromAFileAndWritesOnlyJsonToANewOutputFile()
    {
        string arguments = JsonFile("arguments.json", """{"operation":"normalize","date":"1 JAN 1800"}""");
        string output = Path.Combine(_directory, "result.json");

        var result = await Run(["call", "date_calc", "--args", arguments, "--out", output]);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Output);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal("normalize", json.RootElement.GetProperty("operation").GetString());
        Assert.False(File.ReadAllBytes(output).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public async Task Call_ExistingOutputIsRefusedBeforeApplying()
    {
        string document = Document();
        byte[] before = File.ReadAllBytes(document);
        string changeset = JsonFile("changeset.json", Changeset);
        string output = JsonFile("result.json", "keep this");
        string arguments = JsonSerializer.Serialize(new { changesetPath = changeset, items = "all" });

        var result = await Run(["call", "apply_changeset", "--input", document, "--args", "-", "--out", output], arguments);

        Assert.Equal(1, result.Exit);
        Assert.Equal("keep this", File.ReadAllText(output));
        Assert.Equal(before, File.ReadAllBytes(document));
    }

    [Fact]
    public async Task Call_ReadOnlyRefusesApplyAndPreservesTheDocument()
    {
        string document = Document();
        byte[] before = File.ReadAllBytes(document);
        string changeset = JsonFile("changeset.json", Changeset);

        var result = await Run(["call", "apply_changeset", "--input", document, "--read-only", "--args", "-"],
            JsonSerializer.Serialize(new { changesetPath = changeset, items = "all" }));

        Assert.Equal(1, result.Exit);
        Assert.Contains("--read-only", result.Error);
        Assert.Equal(before, File.ReadAllBytes(document));
    }

    [Fact]
    public async Task Call_ReadOnlyAllowsDraftOutputFiles()
    {
        string document = Document();
        byte[] before = File.ReadAllBytes(document);
        string output = Path.Combine(_directory, "draft.json");
        var arguments = new
        {
            findings = new[]
            {
                new
                {
                    id = "F-1", extract = "A synthetic census entry.",
                    source = new { name = "Example census", locator = "p. 1", @class = "original", information = "primary" },
                    target = new { xref = "@I1@" },
                    facts = new[] { new { record = "@I1@", fact = "CENS", date = "1850", quay = 3 } },
                },
            },
            outPath = output,
        };

        var result = await Run(["call", "draft_changeset", "--input", document, "--read-only", "--args", "-"],
            JsonSerializer.Serialize(arguments));

        Assert.Equal(0, result.Exit);
        Assert.True(File.Exists(output));
        Assert.Equal(before, File.ReadAllBytes(document));
    }

    [Fact]
    public async Task Call_PolicyViolationRefusesApply()
    {
        string document = Document();
        byte[] before = File.ReadAllBytes(document);
        string changeset = JsonFile("changeset.json", Changeset);
        string policy = JsonFile("policy.json", """{"bannedPhrases":["Synthetic evidence"]}""");

        var result = await Run(["call", "apply_changeset", "--input", document, "--policy", policy, "--args", "-"],
            JsonSerializer.Serialize(new { changesetPath = changeset, items = "all" }));

        Assert.Equal(1, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("banned phrase", result.Output);
        Assert.Equal(before, File.ReadAllBytes(document));
    }

    [Fact]
    public async Task Call_PrivacyFiltersTheInitialDocument()
    {
        string document = Document(Ged.Replace("1 SEX F", "1 SEX F\n1 RESN CONFIDENTIAL"));

        var result = await Run(["call", "get_record", "--input", document, "--enforce-privacy", "--args", "-"],
            """{"xref":"@I1@"}""");

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain("Mara", result.Output);
    }

    [Fact]
    public async Task Call_ApplyUsesTheExistingEngineAndReturnsStructuredJson()
    {
        string document = Document();
        string changeset = JsonFile("changeset.json", Changeset);

        var result = await Run(["call", "apply_changeset", "--input", document, "--args", "-"],
            JsonSerializer.Serialize(new { changesetPath = changeset, items = "all" }));

        Assert.Equal(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("1 NOTE Synthetic evidence.", File.ReadAllText(document));
    }

    [Fact]
    public async Task Call_FailedValidationReturnsJsonAndANonzeroExitCode()
    {
        string document = Document();
        byte[] before = File.ReadAllBytes(document);
        string changeset = JsonFile("changeset.json", Changeset.Replace("@I1@", "@I999@"));

        var result = await Run(["call", "validate_changeset", "--input", document, "--args", "-"],
            JsonSerializer.Serialize(new { changesetPath = changeset, items = "all" }));

        Assert.Equal(1, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(document));
    }

    [Fact]
    public async Task Call_FailedConformanceCheckReturnsANonzeroExitCode()
    {
        string document = Document(Ged.Replace("1 SEX F", "1 SEX F\n1 FAMC @F999@"));

        var result = await Run(["call", "validate_document", "--input", document]);

        Assert.Equal(1, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task Call_WrongArgumentTypeFailsWithoutAnUnhandledException()
    {
        var result = await Run(["call", "date_calc", "--args", "-"], """{"operation":{}}""");

        Assert.Equal(1, result.Exit);
        Assert.NotEmpty(result.Error);
    }
}
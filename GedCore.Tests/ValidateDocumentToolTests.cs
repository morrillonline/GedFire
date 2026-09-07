using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

/// <summary>
/// Unit-level coverage for validate_document: it re-reads the bound file
/// fresh on every call (see ValidateDocumentTool), so these tests use real
/// temporary files rather than a DocumentSession/snapshot.
/// </summary>
public class ValidateDocumentToolTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-validate-document-tool-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    const string CleanGed70 = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        0 TRLR

        """;

    // An undeclared extension tag (not listed in HEAD.SCHMA) trips GED010
    // as a Warning without being a structural error.
    const string GedWithWarning = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 _CUSTOMTAG something
        0 TRLR

        """;

    static JsonElement StructuredContent(CallToolResult result) => result.StructuredContent!.Value;

    static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    string WriteGed(string gedText)
    {
        string path = Path.Combine(_dir, Guid.NewGuid() + ".ged");
        File.WriteAllText(path, gedText);
        return path;
    }

    [Fact]
    public async Task HandleAsync_CleanDocument_PassesWithNoDiagnostics()
    {
        var tool = new ValidateDocumentTool(WriteGed(CleanGed70), new ToolGate());

        var result = await tool.HandleAsync(warningsAsErrors: false, CancellationToken.None);

        Assert.False(result.IsError);
        var root = StructuredContent(result);
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task HandleAsync_WarningOnly_PassesByDefault()
    {
        var tool = new ValidateDocumentTool(WriteGed(GedWithWarning), new ToolGate());

        var result = await tool.HandleAsync(warningsAsErrors: false, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.True(root.GetProperty("warningCount").GetInt32() > 0);
        Assert.NotEmpty(root.GetProperty("diagnostics").EnumerateArray());
        var first = root.GetProperty("diagnostics").EnumerateArray().First();
        Assert.Equal("Warning", first.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task HandleAsync_WarningOnly_FailsWhenWarningsAsErrors()
    {
        var tool = new ValidateDocumentTool(WriteGed(GedWithWarning), new ToolGate());

        var result = await tool.HandleAsync(warningsAsErrors: true, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.False(root.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_TextFallback_MatchesStructuredContentCompactly()
    {
        var tool = new ValidateDocumentTool(WriteGed(CleanGed70), new ToolGate());

        var result = await tool.HandleAsync(warningsAsErrors: false, CancellationToken.None);

        string text = TextOf(result);
        Assert.Equal(
            JsonSerializer.Serialize(StructuredContent(result), CallToolResults.JsonOptions),
            text);
        Assert.DoesNotContain("\n", text);
    }

    [Fact]
    public async Task HandleAsync_MissingFile_ReturnsIsError()
    {
        string path = Path.Combine(_dir, "gone.ged");
        var tool = new ValidateDocumentTool(path, new ToolGate());

        var result = await tool.HandleAsync(warningsAsErrors: false, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains(path, TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_AlreadyCancelledToken_PropagatesRatherThanReturningIsError()
    {
        var tool = new ValidateDocumentTool(WriteGed(CleanGed70), new ToolGate());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.HandleAsync(warningsAsErrors: false, cts.Token));
    }

    [Fact]
    public void ToMcpServerTool_DeclaresNameDescriptionSchemasAndAnnotations()
    {
        var tool = new ValidateDocumentTool(WriteGed(CleanGed70), new ToolGate()).ToMcpServerTool();

        Assert.Equal(ValidateDocumentTool.ToolName, tool.ProtocolTool.Name);
        Assert.Equal(ValidateDocumentTool.Description, tool.ProtocolTool.Description);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations!.IdempotentHint);

        using var expectedInput = JsonDocument.Parse(ValidateDocumentTool.InputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedInput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        using var expectedOutput = JsonDocument.Parse(ValidateDocumentTool.OutputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedOutput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.OutputSchema!.Value));
    }
}

using System.Text.Json;
using GedCore.Ged70;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

/// <summary>
/// Unit-level coverage for validate_changeset and apply_changeset: both
/// tools call ChangesetApplier directly against a real file (no
/// DocumentSession involved — see ChangesetToolSupport), so these tests use
/// real temporary files, the same approach ApplyFileLockingTests takes for
/// ChangesetApplier's path-based overload itself.
/// </summary>
public class ChangesetMcpToolTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-changeset-tool-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    static readonly string[] BaseLines =
    [
        "0 HEAD",
        "1 GEDC",
        "2 VERS 7.0",
        "0 @I00001@ INDI",
        "1 NAME Allen /Test/",
        "1 SEX M",
        "0 TRLR",
    ];

    string WriteGed()
    {
        string path = Path.Combine(_dir, "test.ged");
        var document = Ged70Parser.Parse(string.Join("\r\n", BaseLines) + "\r\n");
        using var stream = File.Create(path);
        Ged70Formatter.Write(document, stream);
        return path;
    }

    const string CreateNoteChangeset = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateNote", "record": "@I00001@", "text": "A note." } ] } ] }
        """;

    const string InvalidTargetChangeset = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateNote", "record": "@I09999@", "text": "A note." } ] } ] }
        """;

    string WriteChangeset(string json)
    {
        string path = Path.Combine(_dir, "changeset.json");
        File.WriteAllText(path, json);
        return path;
    }

    static JsonElement StructuredContent(CallToolResult result) => result.StructuredContent!.Value;

    // -------------------------------------------------------------------
    // validate_changeset
    // -------------------------------------------------------------------

    [Fact]
    public async Task ValidateChangeset_ValidChangeset_SucceedsWithoutWritingTheFile()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(CreateNoteChangeset);
        var tool = new ValidateChangesetTool(gedPath, new ToolGate());

        var result = await tool.HandleAsync(changesetPath, "all", CancellationToken.None);

        Assert.False(result.IsError);
        var structured = StructuredContent(result);
        Assert.True(structured.GetProperty("success").GetBoolean());
        Assert.Empty(structured.GetProperty("errors").EnumerateArray());
        Assert.Empty(structured.GetProperty("deltas").EnumerateObject());
        Assert.Empty(structured.GetProperty("mintedXrefs").EnumerateObject());
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public async Task ValidateChangeset_InvalidTarget_ReturnsErrorsWithoutWritingTheFile()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(InvalidTargetChangeset);
        var tool = new ValidateChangesetTool(gedPath, new ToolGate());

        var result = await tool.HandleAsync(changesetPath, "all", CancellationToken.None);

        Assert.False(result.IsError);
        var structured = StructuredContent(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.NotEmpty(structured.GetProperty("errors").EnumerateArray());
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public async Task ValidateChangeset_MissingChangesetFile_ReturnsIsErrorWithThePath()
    {
        string gedPath = WriteGed();
        string missing = Path.Combine(_dir, "does-not-exist.json");
        var tool = new ValidateChangesetTool(gedPath, new ToolGate());

        var result = await tool.HandleAsync(missing, "all", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(missing, ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task ValidateChangeset_MalformedItems_ReturnsIsError()
    {
        string gedPath = WriteGed();
        string changesetPath = WriteChangeset(CreateNoteChangeset);
        var tool = new ValidateChangesetTool(gedPath, new ToolGate());

        var result = await tool.HandleAsync(changesetPath, "not-a-number", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("items must be", ((TextContentBlock)result.Content[0]).Text);
    }

    // -------------------------------------------------------------------
    // apply_changeset
    // -------------------------------------------------------------------

    [Fact]
    public async Task ApplyChangeset_ValidChangeset_WritesTheFileAndReportsDeltas()
    {
        string gedPath = WriteGed();
        string changesetPath = WriteChangeset(CreateNoteChangeset);
        var tool = new ApplyChangesetTool(gedPath, new ToolGate(), readOnly: false);

        var result = await tool.HandleAsync(changesetPath, "all", CancellationToken.None);

        Assert.False(result.IsError);
        var structured = StructuredContent(result);
        Assert.True(structured.GetProperty("success").GetBoolean());
        var doc = Ged70Parser.ReadFile(gedPath);
        Assert.Equal("A note.", doc.ByXref["@I00001@"].ChildrenByTag("NOTE").Single().Value);
    }

    [Fact]
    public async Task ApplyChangeset_ReadOnly_RefusesWithoutTouchingTheFile()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(CreateNoteChangeset);
        var tool = new ApplyChangesetTool(gedPath, new ToolGate(), readOnly: true);

        var result = await tool.HandleAsync(changesetPath, "all", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("--read-only", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public async Task ApplyChangeset_ReadOnly_RefusesEvenAnInvalidChangeset()
    {
        // --read-only must refuse the call itself, before validation ever
        // runs -- not just happen to also fail for an unrelated reason.
        string gedPath = WriteGed();
        string changesetPath = WriteChangeset(InvalidTargetChangeset);
        var tool = new ApplyChangesetTool(gedPath, new ToolGate(), readOnly: true);

        var result = await tool.HandleAsync(changesetPath, "all", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("--read-only", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task ApplyChangeset_InvalidTarget_ReturnsErrorsWithoutWritingTheFile()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(InvalidTargetChangeset);
        var tool = new ApplyChangesetTool(gedPath, new ToolGate(), readOnly: false);

        var result = await tool.HandleAsync(changesetPath, "all", CancellationToken.None);

        Assert.False(result.IsError);
        var structured = StructuredContent(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public async Task ApplyChangeset_ItemsExcludesTheOnlyItem_AppliesNothing()
    {
        string gedPath = WriteGed();
        byte[] before = File.ReadAllBytes(gedPath);
        string changesetPath = WriteChangeset(CreateNoteChangeset);
        var tool = new ApplyChangesetTool(gedPath, new ToolGate(), readOnly: false);

        var result = await tool.HandleAsync(changesetPath, "2", CancellationToken.None);

        Assert.False(result.IsError);
        var structured = StructuredContent(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Contains(
            structured.GetProperty("errors").EnumerateArray(),
            e => e.GetString()!.Contains("item(s) not in changeset"));
        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }

    [Fact]
    public void Constructors_RejectEmptyGedcomPath()
    {
        Assert.Throws<ArgumentException>(() => new ValidateChangesetTool("", new ToolGate()));
        Assert.Throws<ArgumentException>(() => new ApplyChangesetTool("", new ToolGate(), readOnly: false));
    }
}

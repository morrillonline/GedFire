using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class GetRecordsToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @S1@ SOUR
        1 TITL Parish Register
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        """;

    GetRecordsTool Tool() => new(_sessions.Open(Ged), new ToolGate(), Path.GetTempPath());

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    static async Task<CallToolResult> Call(GetRecordsTool tool, string xrefs) =>
        await tool.HandleAsync(Json(xrefs), CancellationToken.None);

    static JsonElement Records(CallToolResult result) => result.StructuredContent!.Value.GetProperty("records");

    [Fact]
    public async Task GetRecords_MixedXrefs_ReturnsOneRecordPerXrefInOrder()
    {
        var result = await Call(Tool(), "[\"@F1@\",\"@I1@\",\"@S1@\"]");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            ["family", "person", "source"],
            Records(result).EnumerateArray().Select(r => r.GetProperty("recordType").GetString()!));
        Assert.Equal("Cornelius Ashworth", Records(result)[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetRecords_UnknownXref_ReturnsNotFoundInItsSlotWithoutFailingTheCall()
    {
        var result = await Call(Tool(), "[\"@I1@\",\"@I99@\",\"@I2@\"]");

        Assert.NotEqual(true, result.IsError);
        var records = Records(result);
        Assert.Equal("person", records[0].GetProperty("recordType").GetString());
        Assert.Equal("not_found", records[1].GetProperty("recordType").GetString());
        Assert.Equal("@I99@", records[1].GetProperty("xref").GetString());
        Assert.Equal("person", records[2].GetProperty("recordType").GetString());
    }

    [Fact]
    public async Task GetRecords_RepeatedXref_IsReturnedEachTime()
    {
        var result = await Call(Tool(), "[\"@I1@\",\"@I1@\"]");

        Assert.Equal(2, Records(result).GetArrayLength());
    }

    [Fact]
    public async Task GetRecords_EachRecord_MatchesWhatGetRecordReturns()
    {
        var session = _sessions.Open(Ged);
        var single = await new GetRecordTool(session, new ToolGate(), Path.GetTempPath())
            .HandleAsync("@I1@", CancellationToken.None);
        var batch = await new GetRecordsTool(session, new ToolGate(), Path.GetTempPath())
            .HandleAsync(Json("[\"@I1@\"]"), CancellationToken.None);

        Assert.Equal(
            single.StructuredContent!.Value.GetRawText(),
            batch.StructuredContent!.Value.GetProperty("records")[0].GetRawText());
    }

    [Fact]
    public async Task GetRecords_IncludeSources_AddsEachCitedSourceOnceAcrossTheBatch()
    {
        const string ged = """
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @S1@ SOUR
            1 TITL Parish Register
            0 @I1@ INDI
            1 NAME Cornelius /Ashworth/
            1 BIRT
            2 DATE 1741
            2 SOUR @S1@
            0 @I2@ INDI
            1 NAME Beatrice /Fenwick/
            1 BIRT
            2 DATE 1745
            2 SOUR @S1@
            """;
        var tool = new GetRecordsTool(_sessions.Open(ged), new ToolGate(), Path.GetTempPath());

        var withSources = await tool.HandleAsync(Json("[\"@I1@\",\"@I2@\"]"), CancellationToken.None, includeSources: true);
        var without = await tool.HandleAsync(Json("[\"@I1@\",\"@I2@\"]"), CancellationToken.None);

        var sources = withSources.StructuredContent!.Value.GetProperty("sources");
        Assert.Equal("@S1@", Assert.Single(sources.EnumerateArray()).GetProperty("xref").GetString());
        Assert.False(without.StructuredContent!.Value.TryGetProperty("sources", out _));
    }

    [Theory]
    [InlineData("null", "xrefs is required")]
    [InlineData("\"@I1@\"", "xrefs must be an array")]
    [InlineData("[]", "xrefs must contain 1 to 500 xrefs, got 0")]
    [InlineData("[\"@I1@\", 5]", "xrefs[1] must be an xref string")]
    [InlineData("[\"I1\"]", "xrefs[0] must be an xref such as")]
    public async Task GetRecords_BadXrefs_AreRejectedByFieldName(string xrefs, string expected)
    {
        var result = await Call(Tool(), xrefs);

        Assert.True(result.IsError);
        Assert.Contains(expected, ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task GetRecords_MoreThanTheLimit_IsRejected()
    {
        string xrefs = "[" + string.Join(",", Enumerable.Repeat("\"@I1@\"", GetRecordsTool.MaxXrefs + 1)) + "]";

        var result = await Call(Tool(), xrefs);

        Assert.True(result.IsError);
        Assert.Contains("xrefs must contain 1 to 500 xrefs, got 501", ((TextContentBlock)result.Content[0]).Text);
    }

    static async Task<CallToolResult> CallWithFields(GetRecordsTool tool, string xrefs, string fields, bool includeSources = false) =>
        await tool.HandleAsync(Json(xrefs), CancellationToken.None, includeSources, Json(fields));

    static IEnumerable<string> PropertyNames(JsonElement record) => record.EnumerateObject().Select(p => p.Name);

    [Fact]
    public async Task GetRecords_WithFields_ReturnsRecordTypeXrefAndOnlyTheNamedFields()
    {
        var result = await CallWithFields(Tool(), "[\"@I1@\"]", "[\"name\"]");

        Assert.NotEqual(true, result.IsError);
        var record = Records(result)[0];
        Assert.Equal(["recordType", "xref", "name"], PropertyNames(record));
        Assert.Equal("Cornelius Ashworth", record.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetRecords_WithFields_AppliesEachRecordTypesOwnFieldsAndSkipsTheRest()
    {
        var result = await CallWithFields(Tool(), "[\"@I1@\",\"@F1@\",\"@S1@\"]", "[\"name\",\"husband\",\"publication\"]");

        var records = Records(result);
        Assert.Equal(["recordType", "xref", "name"], PropertyNames(records[0]));
        Assert.Equal(["recordType", "xref", "husband"], PropertyNames(records[1]));
        Assert.Equal(["recordType", "xref", "publication"], PropertyNames(records[2]));
    }

    [Fact]
    public async Task GetRecords_WithFields_LeavesNotFoundRecordsAsTheyAre()
    {
        var result = await CallWithFields(Tool(), "[\"@I99@\"]", "[\"name\"]");

        Assert.Equal(["recordType", "xref"], PropertyNames(Records(result)[0]));
        Assert.Equal("not_found", Records(result)[0].GetProperty("recordType").GetString());
    }

    [Fact]
    public async Task GetRecords_WithoutFields_IsUnchanged()
    {
        var whole = await Call(Tool(), "[\"@I1@\"]");
        var explicitNull = await CallWithFields(Tool(), "[\"@I1@\"]", "null");

        Assert.Equal(Records(whole).GetRawText(), Records(explicitNull).GetRawText());
        Assert.Contains("birth", PropertyNames(Records(whole)[0]));
    }

    [Fact]
    public async Task GetRecords_WithFieldsAndIncludeSources_StillReturnsTheTopLevelSources()
    {
        const string cited = Ged + "\n0 @I3@ INDI\n1 NAME Cited /Person/\n2 SOUR @S1@";
        var tool = new GetRecordsTool(_sessions.Open(cited), new ToolGate(), Path.GetTempPath());

        var result = await CallWithFields(tool, "[\"@I3@\"]", "[\"name\"]", includeSources: true);

        Assert.Equal(["recordType", "xref", "name"], PropertyNames(Records(result)[0]));
        Assert.Equal("@S1@", result.StructuredContent!.Value.GetProperty("sources")[0].GetProperty("xref").GetString());
    }

    [Theory]
    [InlineData("\"name\"", "fields must be an array")]
    [InlineData("[]", "fields must name at least one field")]
    [InlineData("[\"name\", 3]", "fields[1] must be a non-blank string")]
    [InlineData("[\"name\", \"nme\"]", "fields[1] \"nme\" is not a record field. Accepted fields:")]
    public async Task GetRecords_BadFields_AreRejectedByFieldName(string fields, string expected)
    {
        var result = await CallWithFields(Tool(), "[\"@I1@\"]", fields);

        Assert.True(result.IsError);
        Assert.Contains(expected, ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public void RecordProjector_AcceptedFields_ExcludeTheTwoAlwaysPresentOnes()
    {
        Assert.DoesNotContain("recordType", RecordProjector.AcceptedFields);
        Assert.DoesNotContain("xref", RecordProjector.AcceptedFields);
        Assert.Contains("name", RecordProjector.AcceptedFields);
        Assert.Contains("husband", RecordProjector.AcceptedFields);
        Assert.Contains("publication", RecordProjector.AcceptedFields);
    }

    [Fact]
    public void Tool_OutputSchemaReusesGetRecordDefinitionsForEveryRecordKind()
    {
        var schema = Json(GetRecordsTool.OutputSchemaJson);

        Assert.Equal(5, schema.GetProperty("properties").GetProperty("records").GetProperty("items").GetProperty("anyOf").GetArrayLength());
        Assert.True(schema.GetProperty("$defs").TryGetProperty("PersonRecord", out _));
    }

    [Fact]
    public void Tool_IsAdvertisedReadOnlyAndIdempotent()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("get_records", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
    }

    [Fact]
    public void Constructor_RejectsNullDependenciesAndBlankMediaDirectory()
    {
        var session = _sessions.Open(Ged);
        Assert.Throws<ArgumentNullException>(() => new GetRecordsTool(null!, new ToolGate(), "m"));
        Assert.Throws<ArgumentNullException>(() => new GetRecordsTool(session, null!, "m"));
        Assert.Throws<ArgumentException>(() => new GetRecordsTool(session, new ToolGate(), ""));
    }
}

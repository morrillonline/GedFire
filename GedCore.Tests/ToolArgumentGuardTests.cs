using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class ToolArgumentGuardTests : IDisposable
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME William /Ashworth/
        """;

    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    ToolArgumentGuard Guard() => new(
    [
        new SelectTargetsTool(_sessions.Open(Ged), new ToolGate()).ToMcpServerTool(),
        new DateCalcTool(new ToolGate()).ToMcpServerTool(),
        new GetDocumentStatsTool(_sessions.Open(Ged), new ToolGate()).ToMcpServerTool(),
    ]);

    static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void Check_AllRequiredPresentAndNothingElse_ReturnsNull()
    {
        Assert.Null(Guard().Check("select_targets", Args("""{"count": 5, "surnames": ["Ashworth"]}""")));
    }

    [Fact]
    public void Check_MissingRequiredParameter_NamesItAndListsAcceptedParameters()
    {
        string? error = Guard().Check("select_targets", Args("""{"count": 5}"""));

        Assert.Equal(
            "select_targets: missing required parameter \"surnames\". Accepted parameters: count (required), surnames (required).",
            error);
    }

    [Fact]
    public void Check_EveryMissingRequiredParameter_IsNamed()
    {
        string? error = Guard().Check("select_targets", Args("{}"));

        Assert.Contains("missing required parameters \"count\", \"surnames\"", error);
    }

    [Fact]
    public void Check_NullForARequiredParameter_CountsAsMissing()
    {
        string? error = Guard().Check("select_targets", Args("""{"count": null, "surnames": ["Ashworth"]}"""));

        Assert.Contains("missing required parameter \"count\"", error);
    }

    [Fact]
    public void Check_UnrecognizedParameter_NamesItAndListsAcceptedParameters()
    {
        string? error = Guard().Check("select_targets", Args("""{"count": 5, "surnames": ["A"], "surname": "B"}"""));

        Assert.Equal(
            "select_targets: unrecognized parameter \"surname\". Accepted parameters: count (required), surnames (required).",
            error);
    }

    [Fact]
    public void Check_MissingAndUnrecognized_AreBothReported()
    {
        string? error = Guard().Check("select_targets", Args("""{"count": 5, "surname": "B"}"""));

        Assert.Contains("missing required parameter \"surnames\"; unrecognized parameter \"surname\"", error);
    }

    [Fact]
    public void Check_SeveralUnrecognizedParameters_AreAllNamedInOrder()
    {
        string? error = Guard().Check("select_targets", Args("""{"count": 5, "surnames": ["A"], "zeta": 1, "alpha": 2}"""));

        Assert.Contains("unrecognized parameters \"alpha\", \"zeta\"", error);
    }

    [Fact]
    public void Check_OptionalParameterNotRequired_IsMarkedAsOptionalInTheAcceptedList()
    {
        string? error = Guard().Check("date_calc", Args("""{"date": "1 JAN 1800"}"""));

        Assert.Contains("missing required parameter \"operation\"", error);
        Assert.Contains("operation (required), date, age, from, to", error);
    }

    [Fact]
    public void Check_ToolWithNoParameters_SaysSoWhenGivenAny()
    {
        string? error = Guard().Check("get_document_stats", Args("""{"verbose": true}"""));

        Assert.Equal(
            "get_document_stats: unrecognized parameter \"verbose\". This tool takes no parameters.", error);
    }

    [Fact]
    public void Check_NullArgumentsOnAToolWithNoParameters_ReturnsNull()
    {
        Assert.Null(Guard().Check("get_document_stats", null));
    }

    [Fact]
    public void Check_NullArgumentsOnAToolWithRequiredParameters_ReportsThemMissing()
    {
        Assert.Contains("missing required parameters \"count\", \"surnames\"", Guard().Check("select_targets", null));
    }

    [Fact]
    public void Check_UnknownTool_IsLeftToTheServer()
    {
        Assert.Null(Guard().Check("no_such_tool", Args("""{"anything": 1}""")));
    }

    [Fact]
    public void Constructor_ReadsEveryRegisteredToolsOwnSchema()
    {
        var guard = new ToolArgumentGuard([new DateCalcTool(new ToolGate()).ToMcpServerTool()]);

        Assert.Null(guard.Check("select_targets", Args("""{"anything": 1}""")));
        Assert.NotNull(guard.Check("date_calc", Args("""{"anything": 1}""")));
    }
}

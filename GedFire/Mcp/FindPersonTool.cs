using System.Text.Json;
using GedCore.Matching;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The find_person MCP tool: trims and validates the query, obtains the
// snapshot from DocumentSession, calls PersonMatcher, and returns the mapped
// outcome. The schemas, hint validation, and result mapping live in their own
// classes; no matching or scoring logic lives here.
// ---------------------------------------------------------------------------

public sealed class FindPersonTool
{
    public const string ToolName = "find_person";

    public const string Description =
        "Resolve a name the user mentioned to a person in this server's GEDCOM. Call this whenever a person is " +
        "known by name but not by xref. A single result includes the person's identity, their child-family xref, " +
        "and the xref of every marriage — childless marriages included — with marriage date and spouse name; " +
        "pass those xrefs to future family detail or research tools when needed. When candidates are returned, " +
        "ask the user which person they mean and call again with any new birth or death year/place, father or " +
        "mother name, spouse name, or marriage year/place. Hints rank only people already recalled by name.";

    const int DefaultMaxResults = 8;
    const int MaximumMaxResults = 20;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly NicknameDirectory _nicknames;

    public FindPersonTool(DocumentSession session, ToolGate gate, NicknameDirectory nicknames)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _nicknames = nicknames ?? throw new ArgumentNullException(nameof(nicknames));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(
            ToolName, Description, FindPersonSchemas.InputSchemaJson, FindPersonSchemas.OutputSchemaJson,
            ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    // hints and maxResults arrive as raw JsonElements: a value of the wrong type would otherwise
    // fail inside the SDK binder, before any tool code runs, and surface only as a generic
    // "error occurred". hints needs a real default so the binder treats it as optional.
    Task<CallToolResult> InvokeAsync(
        string query, JsonElement? hints = null, JsonElement? maxResults = null, CancellationToken cancellationToken = default)
    {
        if (!FindPersonHintsReader.TryRead(hints ?? default, out var hintsArgs, out string? error) ||
            !ToolArguments.TryReadOptionalInt(
                maxResults ?? default, "maxResults", 1, MaximumMaxResults, DefaultMaxResults, out int max, out error))
            return Task.FromResult(CallToolResults.Error(error!));

        return HandleAsync(query, hintsArgs, cancellationToken, max);
    }

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP protocol machinery.
    /// Never throws, except that an already-requested cancellation propagates.
    /// </summary>
    public Task<CallToolResult> HandleAsync(
        string query, FindPersonHintsArgs? hints, CancellationToken cancellationToken, int maxResults = DefaultMaxResults) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(query, hints, maxResults, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(
        string query, FindPersonHintsArgs? hints, int maxResults, CancellationToken cancellationToken)
    {
        if ((query ?? "").Trim().Length == 0)
            return CallToolResults.Error("query must not be blank.");

        if (maxResults is < 1 or > MaximumMaxResults)
            return CallToolResults.Error($"maxResults must be an integer between 1 and {MaximumMaxResults}.");

        if (!FindPersonHintsValidator.TryValidate(hints, out string? hintsError))
            return CallToolResults.Error(hintsError!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        // The matcher normalizes the query on its own terms, so it gets the original, untrimmed text.
        var outcome = new PersonMatcher(_nicknames).Match(
            snapshot.MatchIndex, query!, FindPersonHintsMapper.ToMatchHints(hints), maxResults);

        return CallToolResults.Success(FindPersonResultMapper.Map(outcome), CallToolResults.JsonOptions);
    }
}

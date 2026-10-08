using System.Text.Json;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The find_source MCP tool: tells a caller whether a source already exists
// before a changeset creates one. Validates the three optional criteria,
// obtains the snapshot from DocumentSession, and delegates matching to
// SourceFinder.
// ---------------------------------------------------------------------------

public sealed class FindSourceTool
{
    public const string ToolName = "find_source";

    public const string Description =
        "Find existing sources in this server's GEDCOM by title, author, or web address, so a changeset can cite " +
        "a source that already exists instead of creating a duplicate. Each of title, author, and url is a " +
        "case-insensitive substring; supply at least one, and when several are supplied a source must match all " +
        "of them. url matches the web address recorded in the source's note. Returns the matching sources with " +
        "their xrefs, or an empty array when none match.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "title": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Text to find within the source title, ignoring case."
            },
            "author": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Text to find within the source author, ignoring case."
            },
            "url": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Text to find within the web address recorded for the source, ignoring case. A full address or any part of one."
            }
          },
          "required": []
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "sources": {
              "type": "array",
              "description": "The matching sources ordered by title then xref; empty when none match.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                  "title": { "type": ["string", "null"], "description": "The source title; null when absent." },
                  "author": { "type": ["string", "null"], "description": "The source author; null when absent." },
                  "url": { "type": ["string", "null"], "description": "The web address recorded in the source's note; null when none." }
                },
                "required": ["xref", "title", "author", "url"]
              }
            }
          },
          "required": ["sources"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;

    public FindSourceTool(DocumentSession session, ToolGate gate)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by
    // field name in ExecuteAsync rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? title = null, JsonElement? author = null, JsonElement? url = null,
        CancellationToken cancellationToken = default)
        => HandleAsync(title ?? default, author ?? default, url ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement title, JsonElement author, JsonElement url, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(title, author, url, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(
        JsonElement title, JsonElement author, JsonElement url, CancellationToken cancellationToken)
    {
        if (!TryReadCriterion(title, "title", out string? titleText, out string? error) ||
            !TryReadCriterion(author, "author", out string? authorText, out error) ||
            !TryReadCriterion(url, "url", out string? urlText, out error))
            return CallToolResults.Error(error!);

        var query = new SourceQuery(titleText, authorText, urlText);
        if (query.IsEmpty)
            return CallToolResults.Error("find_source needs at least one of title, author, or url.");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var sources = SourceFinder.Find(snapshot.Model, query)
            .Select(s => new FoundSourceResult(s.Xref, s.Title, s.Author, s.Url))
            .ToList();
        return CallToolResults.Success(new FindSourceResult(sources), CallToolResults.JsonOptions);
    }

    static bool TryReadCriterion(JsonElement element, string field, out string? value, out string? error)
    {
        if (!ToolArguments.TryReadOptionalString(element, field, out value, out error)) return false;

        value = value?.Trim();
        if (value is { Length: 0 })
        {
            error = $"{field} must not be blank.";
            return false;
        }
        return true;
    }
}

public sealed record FoundSourceResult(string Xref, string? Title, string? Author, string? Url);

public sealed record FindSourceResult(IReadOnlyList<FoundSourceResult> Sources);

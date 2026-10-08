using System.Text.Json;
using GedFire.TargetSelection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The select_targets MCP tool: the `gedfire select-targets` command as a tool.
// Same gap detection, same draw, same document as the command's wanted.json,
// returned as the result instead of written to a file and without the GEDCOM
// path. The draw is seeded from the clock exactly as the command's is.
// ---------------------------------------------------------------------------

public sealed class SelectTargetsTool
{
    public const string ToolName = "select_targets";

    public const string Description =
        "Draw research targets from this server's GEDCOM: people and families with a missing fact or " +
        "relationship (a missing parent, spouse, or child, or a date or place that is not exact) among the " +
        "people who bear, or are married to someone who bears, one of the given surnames. Returns \"count\" " +
        "targets drawn at random, each with its gap type, difficulty band, points, and the facts needed to " +
        "start work, including possibleDuplicateOf (the best few people the duplicate test pairs with the target) and possibleDuplicateCount; the " +
        "draw keeps at most one Legendary-band target, so a pack can come up short. This is " +
        "the same draw as the gedfire select-targets command. People born within the last 100 years are " +
        "never selected.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "count": {
              "type": "integer",
              "minimum": 1,
              "description": "How many targets to draw."
            },
            "surnames": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Surnames to draw targets for, e.g. [\"Ashworth\", \"Ashwirth\"]."
            }
          },
          "required": ["count", "surnames"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "generated": { "type": "string", "description": "UTC date of the draw, yyyy-MM-dd." },
            "surnames": { "type": "array", "items": { "type": "string" } },
            "totalCandidates": { "type": "integer", "minimum": 0, "description": "Every gap found before the draw." },
            "count": { "type": "integer", "minimum": 0, "description": "Targets actually drawn; can be below the requested count." },
            "targets": { "type": "array", "items": { "type": "object" } },
            "draw": {
              "type": "object",
              "properties": {
                "seed": { "type": "integer" },
                "legendaryDiscards": { "type": "array", "items": { "type": "object" } }
              },
              "required": ["seed", "legendaryDiscards"]
            }
          },
          "required": ["generated", "surnames", "totalCandidates", "count", "targets", "draw"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;

    public SelectTargetsTool(DocumentSession session, ToolGate gate)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyNonIdempotent)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by
    // field name in ExecuteAsync rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? count = null, JsonElement? surnames = null, CancellationToken cancellationToken = default)
        => HandleAsync(count ?? default, surnames ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement count, JsonElement surnames, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(count, surnames, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(JsonElement count, JsonElement surnames, CancellationToken cancellationToken)
    {
        if (count.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return CallToolResults.Error("count is required: a positive integer.");
        if (!ToolArguments.TryReadOptionalInt(count, "count", 1, int.MaxValue, 1, out int drawCount, out string? error))
            return CallToolResults.Error(error!);
        if (!ToolArguments.TryReadOptionalStringList(surnames, "surnames", out var surnameList, out error))
            return CallToolResults.Error(error!);
        if (surnameList.Count == 0)
            return CallToolResults.Error("surnames is required: an array with at least one surname such as [\"Ashworth\"].");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var candidates = GapDetector.Detect(snapshot.Model, surnameList);
        var draw = DuplicateAnnotator.Annotate(
            TargetDrawer.Draw(candidates, drawCount, DateTime.UtcNow.Ticks), snapshot.MatchIndex, cancellationToken);

        string json = WantedFileWriter.ToJson(surnameList, candidates.Count, draw);
        using var document = JsonDocument.Parse(json);
        return CallToolResults.Success(document.RootElement.Clone(), CallToolResults.JsonOptions);
    }
}

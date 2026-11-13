using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Policy;
using GedFire.Schemas;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The validate_finding MCP tool: checks finding files (or findings entries)
// without drafting a changeset, so a writer can gate its files before
// draft_changeset. Read-only: never writes the document or any other file.
// Bound to a document it also resolves every xref; without one (gedfire
// tools call without --input) it checks shape alone.
// ---------------------------------------------------------------------------

public sealed class ValidateFindingTool
{
    public const string ToolName = "validate_finding";

    public const string Description =
        "Check findings the way draft_changeset takes them, without drafting: finding files by findingPaths " +
        "(the finding-file schema: draft, argument, draftGaps, record and the caller's meta; any other top-level key, or " +
        "a draft.id that differs from the file name, is refused), or draft entries by findings or inputPath. Per finding " +
        "it returns valid and errors (by field path), readyToDraft, draftGaps, and bindings: each xref the draft or a " +
        "record person names, with exists, name and lifespan for a document record or declared for a @New...@ " +
        "placeholder. Every rule the finding alone decides is checked with or without a document, and even while " +
        "draftGaps keep it from drafting: field shapes, GEDCOM 7 dates, fact tags, QUAY ceilings, placeholder " +
        "declarations, sex on a new partner or parent, verbatim excerpts, and one grade per quoted text. Against the " +
        "server's document a finding that is ready is also compiled on its own by draft_changeset's own checks, so the " +
        "errors are the ones draft_changeset gives for the same fault and readyToDraft is true exactly when " +
        "draft_changeset compiles that file without error; each record person's xref must name a person. A file with no " +
        "draft or with draftGaps is not ready, and is an error only with requireDraft. Writes nothing.";

    public static readonly string InputSchemaJson = SchemaComposer.Compose("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "findingPaths": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Paths to finding files. Give findingPaths, or findings or inputPath, not both."
            },
            "findings": {
              "type": "array",
              "minItems": 1,
              "description": "Draft entries, the same array draft_changeset takes.",
              "items": {}
            },
            "inputPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Path to a JSON file holding {\"findings\": [...]}. Give findings or inputPath, not both."
            },
            "requireDraft": {
              "type": "boolean",
              "description": "Report a file with no draft, or with draftGaps, as an error rather than as valid but not ready. Default false."
            }
          }
        }
        """, schema => schema["properties"]!["findings"]!["items"] = JsonNode.Parse(FindingSchemas.FindingJson));

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean", "description": "true when every finding is valid." },
            "documentChecked": { "type": "boolean", "description": "true when xrefs were resolved against a document; false when only the rules that need no document were checked." },
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "file": { "type": ["string", "null"], "description": "The finding file's full path; null for a findings entry." },
                  "id": { "type": ["string", "null"], "description": "draft.id as written." },
                  "valid": { "type": "boolean" },
                  "errors": { "type": "array", "items": { "type": "string" } },
                  "readyToDraft": { "type": "boolean", "description": "true when draft_changeset would compile this finding without error." },
                  "draftGaps": { "type": "array", "items": { "type": "string" } },
                  "bindings": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "ref": { "type": "string" },
                        "at": { "type": "string", "description": "Where the file names it, e.g. draft.facts[0].record or record.persons[1].xref." },
                        "exists": { "type": "boolean", "description": "Whether the document holds the record; absent for a placeholder or without a document." },
                        "name": { "type": "string", "description": "The person's name, or the source's title." },
                        "lifespan": { "type": "string", "description": "A person's birth-death years, ? for an unknown end." },
                        "declared": { "type": "boolean", "description": "For a placeholder: declared in people, or named as a relation's family." }
                      },
                      "required": ["ref", "at"]
                    }
                  },
                  "schemaVersion": { "type": "string", "description": "The finding-file (or, for an entry, finding) schema version checked against." }
                },
                "required": ["file", "id", "valid", "errors", "readyToDraft", "draftGaps", "bindings", "schemaVersion"]
              }
            }
          },
          "required": ["success", "documentChecked", "findings"]
        }
        """;

    readonly DocumentSession? _session;
    readonly ToolGate _gate;
    readonly ChangesetPolicy? _policy;

    /// <summary>A tool bound to <paramref name="session"/> resolves xrefs; with no session it checks shape alone.</summary>
    public ValidateFindingTool(DocumentSession? session, ToolGate gate, ChangesetPolicy? policy = null)
    {
        _session = session;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _policy = policy;
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    Task<CallToolResult> InvokeAsync(
        JsonElement? findingPaths = null, JsonElement? findings = null, JsonElement? inputPath = null,
        JsonElement? requireDraft = null, CancellationToken cancellationToken = default) =>
        HandleAsync(new ValidateFindingArguments(findingPaths ?? default, findings ?? default, inputPath ?? default, requireDraft ?? default),
            cancellationToken);

    public Task<CallToolResult> HandleAsync(ValidateFindingArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(arguments, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(ValidateFindingArguments arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryReadOptionalBool(arguments.RequireDraft, "requireDraft", out bool requireDraft, out string? error))
            return CallToolResults.Error(error!);
        bool hasPaths = !IsAbsent(arguments.FindingPaths);
        if (hasPaths && (!IsAbsent(arguments.Findings) || !IsAbsent(arguments.InputPath)))
            return CallToolResults.Error("give findingPaths, or findings or inputPath, not both.");

        var model = _session is null ? null : (await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Model;
        var validator = new FindingValidator(model, _policy, requireDraft);
        IReadOnlyList<FindingValidation> results;
        if (hasPaths)
        {
            if (!ToolArguments.TryReadOptionalStringList(arguments.FindingPaths, "findingPaths", out var names, out error))
                return CallToolResults.Error(error!);
            if (names.Count == 0) return CallToolResults.Error("findingPaths must name at least one finding file.");
            results = [.. names.Select(name => validator.Validate(FindingFileReader.ReadPath(Path.GetFullPath(name))))];
        }
        else
        {
            if (!ArgumentFile.TryResolve(arguments.Findings, "findings", arguments.InputPath, "inputPath", out var entries, out error))
                return CallToolResults.Error(error!);
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
                return CallToolResults.Error("give findingPaths, findings or inputPath: a non-empty array of finding entries or files.");
            results = [.. entries.EnumerateArray().Select((entry, index) => validator.Validate(entry, index))];
        }

        return CallToolResults.Success(
            new ValidateFindingResult(results.All(r => r.Valid), model is not null, results), CallToolResults.JsonOptions);
    }

    static bool IsAbsent(JsonElement element) => element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
}

public sealed record ValidateFindingArguments(JsonElement FindingPaths, JsonElement Findings, JsonElement InputPath, JsonElement RequireDraft);

public sealed record ValidateFindingResult(bool Success, bool DocumentChecked, IReadOnlyList<FindingValidation> Findings);

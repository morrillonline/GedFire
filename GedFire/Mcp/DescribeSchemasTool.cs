using System.Text.Json;
using System.Text.Json.Nodes;
using GedFire.Schemas;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The describe_schemas MCP tool: the same versioned finding-file schemas
// `gedfire schema` prints, for a caller that reaches GedFire only over MCP.
// Static reference data; never reads the bound document.
// ---------------------------------------------------------------------------

public sealed class DescribeSchemasTool
{
    public const string ToolName = "describe_schemas";

    public const string Description =
        "Return the versioned JSON Schemas (draft 2020-12) of the finding-file contract: finding (one draft_changeset " +
        "findings entry), finding-file (the file findingPaths and validate_finding read: draft, argument, draftGaps, " +
        "record and the caller's meta), record (match_record's persons, relationships and event) and argument (a " +
        "finding's identity argument). Each carries its $id and x-schemaVersion: a new optional field or enum value " +
        "bumps the minor version; a removal, rename, new required field or tighter constraint bumps the major. " +
        "Pass name for one schema. Never reads the bound document.";

    public static readonly string InputSchemaJson = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "name": { "type": "string", "enum": [{{string.Join(", ", SchemaCatalog.Names.Select(n => $"\"{n}\""))}}], "description": "The one schema to return; omit for all." }
          }
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "schemas": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "name": { "type": "string" },
                  "version": { "type": "string", "description": "<major>.<minor>, the schema's x-schemaVersion." },
                  "id": { "type": "string", "description": "The schema's $id; it changes with the major version." },
                  "schema": { "type": "object", "description": "The standalone JSON Schema document." }
                },
                "required": ["name", "version", "id", "schema"]
              }
            }
          },
          "required": ["schemas"]
        }
        """;

    readonly ToolGate _gate;

    public DescribeSchemasTool(ToolGate gate) =>
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    Task<CallToolResult> InvokeAsync(JsonElement? name = null, CancellationToken cancellationToken = default) =>
        HandleAsync(name ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(JsonElement name, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, _ => Task.FromResult(Execute(name)), cancellationToken);

    static CallToolResult Execute(JsonElement name)
    {
        if (!ToolArguments.TryReadOptionalString(name, "name", out string? wanted, out string? error))
            return CallToolResults.Error(error!);
        IReadOnlyList<PublishedSchema> schemas = SchemaCatalog.All;
        if (wanted is not null)
        {
            if (SchemaCatalog.Find(wanted) is not { } one)
                return CallToolResults.Error($"name must be one of {string.Join(", ", SchemaCatalog.Names)}, got \"{wanted}\".");
            schemas = [one];
        }
        return CallToolResults.Success(
            new DescribeSchemasResult([.. schemas.Select(s => new DescribedSchema(s.Name, s.Version, s.Id, s.Document()))]),
            CallToolResults.JsonOptions);
    }
}

public sealed record DescribeSchemasResult(IReadOnlyList<DescribedSchema> Schemas);

public sealed record DescribedSchema(string Name, string Version, string Id, JsonObject Schema);

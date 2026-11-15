using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The describe_changeset_ops MCP tool: returns the changeset envelope shape,
// its structures and the full op dialect catalog (see ChangesetOpsCatalog) so an agent
// can compose a valid changeset for validate_changeset/apply_changeset
// without external documentation or trial-and-error against error messages.
// Pure static reference data -- no DocumentSession, no bound-file argument,
// available regardless of --read-only.
// ---------------------------------------------------------------------------

public sealed class DescribeChangesetOpsTool
{
    public const string ToolName = "describe_changeset_ops";

    public const string Description =
        "Return the changeset contract that validate_changeset and apply_changeset both consume and draft_changeset " +
        "writes: the envelope (schemaVersion/proposal/newSources/items/findings), every structure an op or the " +
        "envelope holds (citation, people entry, marriage, fact match…), and every op in the dialect (createOrUpdate/" +
        "delete x Vital|Spouse|Child|Parent|Source|Citation|Note|Media, plus mergePerson), each with its required and " +
        "optional fields and one worked example. Generated from the same table as the published changeset schema " +
        "(describe_schemas, changeset), so the two cannot drift. Call this before composing a changeset from scratch, " +
        "or whenever unsure which fields an op takes -- it is cheaper and more reliable than guessing and reading " +
        "validate_changeset's error text. Takes no arguments and never reads the bound document; the dialect " +
        "is the same for every gedfire mcp server.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {},
          "required": []
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "envelope": {
              "type": "object",
              "additionalProperties": false,
              "description": "The changeset file's own wrapper, independent of any op.",
              "properties": {
                "schemaVersion": { "type": "string", "description": "The contract version a changeset written today names in its schemaVersion." },
                "description": { "type": "string" },
                "example": { "description": "One complete, valid changeset file." }
              },
              "required": ["schemaVersion", "description", "example"]
            },
            "structures": {
              "type": "array",
              "description": "Every object the envelope and the ops hold, by the name an op's field type gives.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "name": { "type": "string" },
                  "description": { "type": "string" },
                  "fields": { "$ref": "#/$defs/fields" }
                },
                "required": ["name", "description", "fields"]
              }
            },
            "ops": {
              "type": "array",
              "description": "Every op kind the v2 dialect defines.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "op": { "type": "string", "description": "The exact \"op\" value to use in a changeset." },
                  "verb": { "type": "string", "enum": ["createOrUpdate", "delete", "merge"] },
                  "noun": { "type": "string" },
                  "summary": { "type": "string", "description": "What this op does and its idempotency/no-op rule." },
                  "fields": { "$ref": "#/$defs/fields" },
                  "example": { "description": "One complete, valid op object for this \"op\" value." }
                },
                "required": ["op", "verb", "noun", "summary", "fields", "example"]
              }
            }
          },
          "required": ["envelope", "structures", "ops"],
          "$defs": {
            "fields": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "name": { "type": "string" },
                  "type": { "type": "string", "description": "string, integer, boolean, array of string, object (<structure>), array of <structure>, or array of op; an enumerated string lists its values." },
                  "required": { "type": "boolean" },
                  "description": { "type": "string" }
                },
                "required": ["name", "type", "required", "description"]
              }
            }
          }
        }
        """;

    readonly ToolGate _gate;

    public DescribeChangesetOpsTool(ToolGate gate) =>
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    // The delegate McpServerTool.Create binds arguments to and invokes.
    // There are no business parameters -- only the SDK-injected CancellationToken.
    Task<CallToolResult> InvokeAsync(CancellationToken cancellationToken = default) => HandleAsync(cancellationToken);

    public Task<CallToolResult> HandleAsync(CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, _ => Task.FromResult(Execute()), cancellationToken);

    static CallToolResult Execute()
    {
        var result = new DescribeChangesetOpsResult(ChangesetOpsCatalog.Envelope, ChangesetOpsCatalog.Structures, ChangesetOpsCatalog.Ops);
        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }
}

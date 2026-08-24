using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The describe_changeset_ops MCP tool: returns the changeset envelope shape
// and the full v2 op dialect catalog (see ChangesetOpsCatalog) so an agent
// can compose a valid changeset for validate_changeset/apply_changeset
// without external documentation or trial-and-error against error messages.
// Pure static reference data -- no DocumentSession, no bound-file argument,
// available regardless of --read-only.
// ---------------------------------------------------------------------------

public sealed class DescribeChangesetOpsTool
{
    public const string ToolName = "describe_changeset_ops";

    public const string Description =
        "Return the changeset file format that validate_changeset and apply_changeset both consume: the " +
        "envelope shape (proposal/newSources/items) and every op in the v2 dialect (createOrUpdate/delete x " +
        "Vital|Spouse|Child|Parent|Source|Citation|Note|Media, plus mergePerson), each with its required and " +
        "optional fields and one worked example. Call this before composing a changeset from scratch, or " +
        "whenever unsure which fields an op takes -- it is cheaper and more reliable than guessing and reading " +
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
                "description": { "type": "string" },
                "example": { "description": "One complete, valid changeset file." }
              },
              "required": ["description", "example"]
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
                  "fields": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "name": { "type": "string" },
                        "type": { "type": "string" },
                        "required": { "type": "boolean" },
                        "description": { "type": "string" }
                      },
                      "required": ["name", "type", "required", "description"]
                    }
                  },
                  "example": { "description": "One complete, valid op object for this \"op\" value." }
                },
                "required": ["op", "verb", "noun", "summary", "fields", "example"]
              }
            }
          },
          "required": ["envelope", "ops"]
        }
        """;

    readonly ToolGate _gate;

    public DescribeChangesetOpsTool(ToolGate gate) =>
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    public McpServerTool ToMcpServerTool()
    {
        var createOptions = new McpServerToolCreateOptions
        {
            Name = ToolName,
            Description = Description,
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
        };

        var tool = McpServerTool.Create(InvokeAsync, createOptions);
        tool.ProtocolTool.Description = Description;
        tool.ProtocolTool.InputSchema = JsonDocument.Parse(InputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse(OutputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
        };
        return tool;
    }

    // The delegate McpServerTool.Create binds arguments to and invokes.
    // There are no business parameters -- only the SDK-injected CancellationToken.
    Task<CallToolResult> InvokeAsync(CancellationToken cancellationToken = default) => HandleAsync(cancellationToken);

    public async Task<CallToolResult> HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(_ => Task.FromResult(Execute()), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CallToolResults.Error($"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    static CallToolResult Execute()
    {
        var result = new DescribeChangesetOpsResult(ChangesetOpsCatalog.Envelope, ChangesetOpsCatalog.Ops);
        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }
}

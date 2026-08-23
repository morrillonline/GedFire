using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The get_document_stats MCP tool. Declares the tool's metadata and schemas;
// takes no arguments; obtains the snapshot from DocumentSession and reads
// personCount/familyCount/gedVersion straight off it. No matching or
// scoring logic — there is none to have.
// ---------------------------------------------------------------------------

public sealed class GetDocumentStatsTool
{
    public const string ToolName = "get_document_stats";

    public const string Description =
        "Report basic size and format facts about this server's bound GEDCOM document: how many people and " +
        "families it contains, and its declared GEDCOM version. Call this for a quick orientation before other " +
        "work, or when the user asks how large their file is or what format it's in. Takes no arguments — there " +
        "is only one bound document.";

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
            "personCount": {
              "type": "integer",
              "minimum": 0,
              "description": "Total number of individual (INDI) records in the document."
            },
            "familyCount": {
              "type": "integer",
              "minimum": 0,
              "description": "Total number of family (FAM) records in the document."
            },
            "gedVersion": {
              "type": ["string", "null"],
              "description": "The document's declared GEDCOM version (HEAD.GEDC.VERS, e.g. \"7.0\" or \"5.5.1\"), or null if the header does not declare one."
            }
          },
          "required": ["personCount", "familyCount", "gedVersion"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;

    public GetDocumentStatsTool(DocumentSession session, ToolGate gate)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <summary>
    /// Build the SDK's McpServerTool for this instance, then overwrite the
    /// advertised description/schemas/annotations with this class's
    /// hand-written, doc-verbatim constants — see FindPersonTool.ToMcpServerTool
    /// for why (the SDK's reflection-derived schema is not the contract).
    /// </summary>
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
    // There are no business parameters — only the SDK-injected CancellationToken.
    Task<CallToolResult> InvokeAsync(CancellationToken cancellationToken = default) => HandleAsync(cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as FindPersonTool.HandleAsync.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ExecuteAsync, cancellationToken).ConfigureAwait(false);
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

    async Task<CallToolResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var result = new DocumentStatsResult(
            snapshot.Model.Individuals.Count,
            snapshot.Model.Families.Count,
            snapshot.GedVersion);

        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }
}

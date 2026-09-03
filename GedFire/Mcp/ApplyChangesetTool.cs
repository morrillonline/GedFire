using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The apply_changeset MCP tool: the only tool on this server that writes to
// the bound document. Runs the identical validation validate_changeset does,
// then -- unless the server was started with --read-only -- applies and
// verifies exactly as `gedfire apply` does, through GedCore.Apply directly.
// A successful write is picked up by DocumentSession's own staleness check
// on the next tool call; this class never touches DocumentSession itself.
// See ChangesetToolSupport for the execution path shared with
// validate_changeset.
// ---------------------------------------------------------------------------

public sealed class ApplyChangesetTool
{
    public const string ToolName = "apply_changeset";

    public const string Description =
        "Apply a proposal changeset to this server's bound GEDCOM document: validate every selected op, apply " +
        "it to an in-memory copy, verify the result (byte-stable round-trip, pointer resolution, record-count " +
        "deltas, no newly created source left uncited), and only then write the file -- there is no window " +
        "where a bad state exists on disk. Call validate_changeset first with the same arguments to preview " +
        "without writing. Refuses to run at all when this server was started with --read-only.";

    public const string InputSchemaJson = ChangesetToolSupport.InputSchemaJson;
    public const string OutputSchemaJson = ChangesetToolSupport.OutputSchemaJson;

    readonly string _absoluteGedcomPath;
    readonly ToolGate _gate;
    readonly bool _readOnly;

    public ApplyChangesetTool(string absoluteGedcomPath, ToolGate gate, bool readOnly)
    {
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _absoluteGedcomPath = absoluteGedcomPath;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _readOnly = readOnly;
    }

    public McpServerTool ToMcpServerTool()
    {
        var createOptions = new McpServerToolCreateOptions
        {
            Name = ToolName,
            Description = Description,
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
        };

        var tool = McpServerTool.Create(InvokeAsync, createOptions);
        tool.ProtocolTool.Description = Description;
        tool.ProtocolTool.InputSchema = JsonDocument.Parse(InputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse(OutputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false,
            DestructiveHint = true,
            IdempotentHint = false,
        };
        return tool;
    }

    // The delegate McpServerTool.Create binds arguments to and invokes.
    Task<CallToolResult> InvokeAsync(string changesetPath, string items, CancellationToken cancellationToken = default)
        => HandleAsync(changesetPath, items, cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure -- including the --read-only refusal --
    /// becomes an isError CallToolResult, the same last-chance-handler
    /// pattern as the other document tools.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(string changesetPath, string items, CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ct => Task.FromResult(Execute(changesetPath, items, ct)), cancellationToken).ConfigureAwait(false);
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

    CallToolResult Execute(string changesetPath, string items, CancellationToken cancellationToken)
    {
        if (_readOnly)
            return CallToolResults.Error(
                "This server was started with --read-only: apply_changeset is disabled. Restart `gedfire mcp` " +
                "without --read-only to enable it, or use validate_changeset to preview the same changeset.");

        return ChangesetToolSupport.Execute(_absoluteGedcomPath, changesetPath, items, dryRun: false, cancellationToken);
    }
}

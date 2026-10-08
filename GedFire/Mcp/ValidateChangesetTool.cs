using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The validate_changeset MCP tool: dry-runs a proposal changeset against the
// bound document without writing anything -- the identical check
// apply_changeset performs before it writes, exposed as its own read-only,
// no-confirmation tool so an agent can preview (and re-preview) a changeset
// freely. Always available, even when the server was started with
// --read-only. See ChangesetToolSupport for the execution path shared with
// apply_changeset.
// ---------------------------------------------------------------------------

public sealed class ValidateChangesetTool
{
    public const string ToolName = "validate_changeset";

    public const string Description =
        "Dry-run a proposal changeset against this server's bound GEDCOM document without writing anything: " +
        "validates every selected op -- target xrefs resolve, fact selectors are unambiguous, cited sources " +
        "resolve, provenance requirements hold, new-person duplicate detection -- and reports the same " +
        "success/log/errors shape apply_changeset would, but the file is never touched. Call this to check a " +
        "changeset, or re-check it after edits, before calling apply_changeset with the same arguments. Always " +
        "available, even on a server started with --read-only.";

    public const string InputSchemaJson = ChangesetToolSupport.InputSchemaJson;
    public const string OutputSchemaJson = ChangesetToolSupport.OutputSchemaJson;

    readonly string _absoluteGedcomPath;
    readonly ToolGate _gate;

    public ValidateChangesetTool(string absoluteGedcomPath, ToolGate gate)
    {
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _absoluteGedcomPath = absoluteGedcomPath;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    // The delegate McpServerTool.Create binds arguments to and invokes.
    Task<CallToolResult> InvokeAsync(string changesetPath, string items, CancellationToken cancellationToken = default)
        => HandleAsync(changesetPath, items, cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as the other document tools.
    /// </summary>
    public Task<CallToolResult> HandleAsync(string changesetPath, string items, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, 
                ct => Task.FromResult(ChangesetToolSupport.Execute(_absoluteGedcomPath, changesetPath, items, dryRun: true, ct)),
                cancellationToken);
}

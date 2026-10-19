using System.Text.Json;
using GedCore.Policy;
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
        "available, even on a server started with --read-only. When the server was started with a policy file, " +
        "every violation of it (banned phrases, a low-graded fact with no verification note, a death place " +
        "with no death evidence) is reported as an error here, so it surfaces at the dry run. Pass \"cards\" to " +
        "get pointsPreview: which research cards the changeset closes by what it writes to the tree (cards may be " +
        "given inline or by cardsPath, a file holding {\"cards\": [...]}). Pass reportPath to write the full result " +
        "JSON to a file a script can read; the response is then only a digest of its counts, unless verbose is true.";

    public static readonly string InputSchemaJson = ChangesetToolSchemas.InputSchemaWithCardsJson;
    public static readonly string OutputSchemaJson = ChangesetToolSchemas.OutputSchemaWithPreviewJson;

    readonly string _absoluteGedcomPath;
    readonly ToolGate _gate;
    readonly ChangesetPolicy? _policy;

    public ValidateChangesetTool(string absoluteGedcomPath, ToolGate gate, ChangesetPolicy? policy = null)
    {
        _policy = policy;
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _absoluteGedcomPath = absoluteGedcomPath;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    // The delegate McpServerTool.Create binds arguments to and invokes.
    Task<CallToolResult> InvokeAsync(
        string changesetPath, string items, JsonElement? cards = null, JsonElement? cardsPath = null,
        JsonElement? reportPath = null, JsonElement? overwrite = null, JsonElement? verbose = null,
        CancellationToken cancellationToken = default)
        => HandleAsync(
            new ValidateChangesetArguments(
                changesetPath, items, cards ?? default, cardsPath ?? default, reportPath ?? default, overwrite ?? default,
                verbose ?? default),
            cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as the other document tools.
    /// </summary>
    public Task<CallToolResult> HandleAsync(string changesetPath, string items, CancellationToken cancellationToken) =>
        HandleAsync(new ValidateChangesetArguments(changesetPath, items), cancellationToken);

    public Task<CallToolResult> HandleAsync(
        string changesetPath, string items, JsonElement cards, CancellationToken cancellationToken) =>
        HandleAsync(new ValidateChangesetArguments(changesetPath, items, cards), cancellationToken);

    public Task<CallToolResult> HandleAsync(
        string changesetPath, string items, JsonElement cards, JsonElement cardsPath, CancellationToken cancellationToken) =>
        HandleAsync(new ValidateChangesetArguments(changesetPath, items, cards, cardsPath), cancellationToken);

    public Task<CallToolResult> HandleAsync(ValidateChangesetArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => Task.FromResult(Execute(arguments, ct)), cancellationToken);

    CallToolResult Execute(ValidateChangesetArguments arguments, CancellationToken cancellationToken)
    {
        if (!ArgumentFile.TryResolve(arguments.Cards, "cards", arguments.CardsPath, "cardsPath", out var cards, out string? error))
            return CallToolResults.Error(error!);
        if (!PointsCardReader.TryRead(cards, out var cardList, out error))
            return CallToolResults.Error(error!);
        if (!ReportFile.TryRead(arguments.ReportPath, arguments.Overwrite, arguments.Verbose, out var report, out error))
            return CallToolResults.Error(error!);

        return ChangesetToolSupport.Execute(
            _absoluteGedcomPath, arguments.ChangesetPath, arguments.Items, dryRun: true, cancellationToken, _policy, cardList,
            report);
    }
}

public sealed record ValidateChangesetArguments(
    string ChangesetPath, string Items, JsonElement Cards = default, JsonElement CardsPath = default,
    JsonElement ReportPath = default, JsonElement Overwrite = default, JsonElement Verbose = default);

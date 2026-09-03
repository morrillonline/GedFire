using System.Text.Json;
using GedCore.Apply;
using GedCore.Validate;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The check_plausibility MCP tool: dry-runs a proposal changeset (the same
// in-memory path validate_changeset uses) and returns just the *new*
// PlausibilityChecker findings the changeset would introduce, structured as
// {code, severity, message, xref} instead of the "conformance note:" log
// lines validate_changeset/apply_changeset already carry. Writes nothing and
// duplicates no rule logic -- see docs/design/plausibility-checker.md's
// Integration section. Like its siblings, it always requires a changeset:
// there is no whole-document mode.
// ---------------------------------------------------------------------------

public sealed class CheckPlausibilityTool
{
    public const string ToolName = "check_plausibility";

    public const string Description =
        "Dry-run a proposal changeset against this server's bound GEDCOM document (the same in-memory path " +
        "validate_changeset uses) and return just the plausibility findings the changeset would newly " +
        "introduce -- a chronological or biological implausibility (e.g. a parent's age at a child's birth, " +
        "an event recorded out of canonical order, a possible duplicate person, an ancestor cycle) that the " +
        "combination of the changeset's new facts and the target's existing facts creates. Structured " +
        "{code, severity, message, xref} records, for a caller that wants to route on severity directly " +
        "rather than parsing validate_changeset's log lines. Writes nothing. Always requires a changeset -- " +
        "there is no whole-document plausibility sweep.";

    public const string InputSchemaJson = ChangesetToolSupport.InputSchemaJson;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": {
              "type": "boolean",
              "description": "Whether the dry run completed. false means errors explains why (e.g. an op failed validation, or a new Error-severity finding -- GEN302 ancestor cycle, or a conformance regression -- would block the changeset); diagnostics may still be empty in that case."
            },
            "log": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Human-readable trace of what was checked, in order -- the same lines validate_changeset produces."
            },
            "errors": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Validation failures. Non-empty only when success is false."
            },
            "diagnostics": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "code": { "type": "string", "description": "Stable rule code, e.g. \"GEN102\"." },
                  "severity": { "type": "string", "enum": ["Warning", "Error"], "description": "Error only for GEN302 (ancestor cycle); every other rule is Warning." },
                  "message": { "type": "string" },
                  "xref": { "type": ["string", "null"], "description": "The INDI or FAM xref the finding is anchored to." }
                },
                "required": ["code", "severity", "message", "xref"]
              },
              "description": "Plausibility findings this changeset would newly introduce -- pre-existing findings the file already carried are never included. Empty when this changeset introduces none, or when success is false because validation failed before any diagnostics could be computed."
            }
          },
          "required": ["success", "log", "errors", "diagnostics"]
        }
        """;

    readonly string _absoluteGedcomPath;
    readonly ToolGate _gate;

    public CheckPlausibilityTool(string absoluteGedcomPath, ToolGate gate)
    {
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _absoluteGedcomPath = absoluteGedcomPath;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

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
    Task<CallToolResult> InvokeAsync(string changesetPath, string items, CancellationToken cancellationToken = default)
        => HandleAsync(changesetPath, items, cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as the other document tools.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(string changesetPath, string items, CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(
                _ => Task.FromResult(Execute(_absoluteGedcomPath, changesetPath, items)),
                cancellationToken).ConfigureAwait(false);
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

    static CallToolResult Execute(string absoluteGedcomPath, string changesetPath, string items)
    {
        string trimmedPath = (changesetPath ?? "").Trim();
        if (trimmedPath.Length == 0)
            return CallToolResults.Error("changesetPath must not be blank.");

        string resolvedChangesetPath = Path.GetFullPath(trimmedPath);
        if (!File.Exists(resolvedChangesetPath))
            return CallToolResults.Error($"Changeset file not found: {changesetPath}");

        var changeset = Changeset.LoadFile(resolvedChangesetPath);

        if (!ItemSelector.TryParse(items ?? "", changeset, out int[] itemNumbers, out string? itemsError))
            return CallToolResults.Error(itemsError!);

        ApplyResult result;
        try
        {
            result = ChangesetApplier.Run(absoluteGedcomPath, changeset, itemNumbers, dryRun: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CallToolResults.Error($"Could not open '{absoluteGedcomPath}': {ex.Message}");
        }

        var diagnostics = result.NewDiagnostics
            .Where(d => d.Code.StartsWith("GEN", StringComparison.Ordinal))
            .Select(d => new PlausibilityFinding(d.Code, d.Severity.ToString(), d.Message, d.Xref))
            .ToList();

        return CallToolResults.Success(
            new CheckPlausibilityResult(result.Success, result.Log, result.Errors, diagnostics),
            CallToolResults.JsonOptions);
    }
}

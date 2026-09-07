using System.Text.Json;
using GedCore;
using GedCore.Validate;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The validate_document MCP tool: runs the same GEDCOM 7 conformance check
// as `gedfire validate` against this server's bound document and reports
// the findings structured, instead of the plain-text lines the CLI prints.
// Re-reads the bound file fresh on every call (like validate_changeset and
// check_plausibility) rather than going through DocumentSession, since
// ConformanceChecker needs the raw parsed GedDocument, not the built
// GedModel a session snapshot carries. Writes nothing.
// ---------------------------------------------------------------------------

public sealed class ValidateDocumentTool
{
    public const string ToolName = "validate_document";

    public const string Description =
        "Run GEDCOM 7 conformance checks against this server's bound document -- the same checks `gedfire " +
        "validate` performs -- and return the findings structured instead of as plain-text lines. Reports tag " +
        "charset and level-hierarchy problems, dangling or misdirected pointers, and other structural " +
        "conformance issues, each anchored to the record and tag it concerns. Call this to sanity-check the " +
        "whole document -- for example before starting a research session on a file from an unfamiliar source, " +
        "or after external edits -- not to validate a proposal changeset (use validate_changeset for that). " +
        "Writes nothing.";

    public const string InputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "warningsAsErrors": {
              "type": "boolean",
              "default": false,
              "description": "If true, a Warning-severity finding also makes passed false, matching `gedfire validate --warnings-as-errors`. The diagnostics list itself is unaffected either way."
            }
          },
          "required": []
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "passed": {
              "type": "boolean",
              "description": "False if any Error-severity finding exists, or any Warning-severity finding exists and warningsAsErrors was true."
            },
            "errorCount": { "type": "integer", "minimum": 0 },
            "warningCount": { "type": "integer", "minimum": 0 },
            "infoCount": { "type": "integer", "minimum": 0 },
            "diagnostics": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "severity": { "type": "string", "enum": ["Error", "Warning", "Info"] },
                  "code": { "type": "string", "description": "Stable rule code, e.g. \"GED001\"." },
                  "message": { "type": "string" },
                  "xref": { "type": ["string", "null"], "description": "The owning level-0 record, if any." },
                  "tag": { "type": "string", "description": "The offending tag." }
                },
                "required": ["severity", "code", "message", "xref", "tag"]
              }
            }
          },
          "required": ["passed", "errorCount", "warningCount", "infoCount", "diagnostics"]
        }
        """;

    readonly string _absoluteGedcomPath;
    readonly ToolGate _gate;

    public ValidateDocumentTool(string absoluteGedcomPath, ToolGate gate)
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
    Task<CallToolResult> InvokeAsync(bool warningsAsErrors = false, CancellationToken cancellationToken = default) =>
        HandleAsync(warningsAsErrors, cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as the other document tools.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(bool warningsAsErrors, CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(
                ct => Task.FromResult(Execute(_absoluteGedcomPath, warningsAsErrors, ct)),
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

    static CallToolResult Execute(string absoluteGedcomPath, bool warningsAsErrors, CancellationToken cancellationToken)
    {
        GedDocument doc;
        try
        {
            doc = GedReader.ReadFile(absoluteGedcomPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CallToolResults.Error($"Could not open '{absoluteGedcomPath}': {ex.Message}");
        }

        var diagnostics = ConformanceChecker.Check(doc, cancellationToken);

        var findings = diagnostics
            .Select(d => new ConformanceFinding(d.Severity.ToString(), d.Code, d.Message, d.Xref, d.Tag))
            .ToList();

        int errorCount = diagnostics.Count(d => d.Severity == GedDiagnosticSeverity.Error);
        int warningCount = diagnostics.Count(d => d.Severity == GedDiagnosticSeverity.Warning);
        int infoCount = diagnostics.Count(d => d.Severity == GedDiagnosticSeverity.Info);
        bool passed = errorCount == 0 && !(warningsAsErrors && warningCount > 0);

        return CallToolResults.Success(
            new ValidateDocumentResult(passed, errorCount, warningCount, infoCount, findings),
            CallToolResults.JsonOptions);
    }
}

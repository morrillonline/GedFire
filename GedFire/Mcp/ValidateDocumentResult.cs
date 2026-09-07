namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The validate_document result shape: a straight mapping of
// GedCore.Validate.ConformanceChecker's diagnostics, the same ones `gedfire
// validate` prints one per line.
// ---------------------------------------------------------------------------

/// <summary>One GEDCOM 7 conformance finding from ConformanceChecker.Check.</summary>
public sealed record ConformanceFinding(
    string Severity,   // "Error", "Warning", or "Info"
    string Code,       // stable, e.g. "GED001"
    string Message,
    string? Xref,      // owning level-0 record, if any
    string Tag);       // offending tag

public sealed record ValidateDocumentResult(
    bool Passed,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    List<ConformanceFinding> Diagnostics);

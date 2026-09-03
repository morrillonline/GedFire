namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The check_plausibility result shape: unlike validate_changeset/
// apply_changeset (which surface new findings as "conformance note:" log
// lines a human reads), this returns the same PlausibilityChecker findings
// structured, for a caller that wants to route on Severity/Code directly.
// ---------------------------------------------------------------------------

/// <summary>One plausibility finding this changeset would newly introduce.</summary>
public sealed record PlausibilityFinding(
    string Code,       // stable, e.g. "GEN102"
    string Severity,   // "Warning" or "Error" (GEN302 only)
    string Message,
    string? Xref);

public sealed record CheckPlausibilityResult(
    bool Success,
    List<string> Log,
    List<string> Errors,
    List<PlausibilityFinding> Diagnostics);

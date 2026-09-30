namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The shared result shape for validate_changeset and apply_changeset: a
// straight mapping of GedCore.Apply.ApplyResult. Deltas and mintedXrefs are
// always empty for validate_changeset (dry-run stops before either is
// computed) and for an apply_changeset run whose ops were all no-ops.
// copiedSources names the source used in place of each shared source an item
// updated (a fresh copy, or an existing source that is exactly the updated one).
// ---------------------------------------------------------------------------

public sealed record ChangesetResult(
    bool Success,
    List<string> Log,
    List<string> Errors,
    Dictionary<string, int> Deltas,
    Dictionary<string, string> MintedXrefs,
    Dictionary<string, string> CopiedSources);

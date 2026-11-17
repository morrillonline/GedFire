using System.Text.Json.Serialization;
using GedCore.Points;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The shared result shape for validate_changeset and apply_changeset: a
// straight mapping of GedCore.Apply.ApplyResult. Deltas and mintedXrefs are
// always empty for validate_changeset (dry-run stops before either is
// computed) and for an apply_changeset run whose ops were all no-ops.
// copiedSources names the source used in place of each shared source an item
// updated (a fresh copy, or an existing source that is exactly the updated one).
// pointsPreview is set only by validate_changeset when the caller supplied cards.
// ---------------------------------------------------------------------------

public sealed record ChangesetResult(
    bool Success,
    List<string> Log,
    List<string> Errors,
    Dictionary<string, int> Deltas,
    Dictionary<string, string> MintedXrefs,
    Dictionary<string, string> CopiedSources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PointsPreview? PointsPreview = null)
{
    /// <summary>The path the result was also written to when reportPath was given.</summary>
    public string? ReportWrittenTo { get; init; }
}

/// <summary>
/// The quiet response validate_changeset and apply_changeset return when reportPath is
/// given without verbose: the counts of the result the report file holds in full, so a
/// run per item does not fill the caller's context with data it will read from the file.
/// </summary>
public sealed record ChangesetRunDigest(
    bool Success,
    IReadOnlyList<int> Items,
    int ErrorCount,
    int LogCount,
    Dictionary<string, int> Deltas,
    int MintedXrefCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PointsPreviewTotal,
    string ReportWrittenTo)
{
    /// <summary>The digest of <paramref name="result"/> after it was written to its report file.</summary>
    public static ChangesetRunDigest Of(ChangesetResult result, IReadOnlyList<int> items) =>
        new(result.Success, items, result.Errors.Count, result.Log.Count, result.Deltas,
            result.MintedXrefs.Count, result.PointsPreview?.Total, result.ReportWrittenTo!);
}

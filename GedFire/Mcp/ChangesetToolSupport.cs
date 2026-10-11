using GedCore.Apply;
using GedCore.Points;
using GedCore.Policy;
using ModelContextProtocol.Protocol;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Shared schema and execution path for validate_changeset and
// apply_changeset: both take the same two arguments and differ only in
// dryRun (and apply_changeset's --read-only gate, applied by its own class
// before this runs). Both call ChangesetApplier.Run directly against the
// server's bound path rather than going through DocumentSession, for two
// reasons, not because DocumentSession's own snapshot could be stale (its
// lazy mtime/length check keeps it current as of any call):
//   1. wrong representation -- DocumentSnapshot.Model is a GedModel, the
//      denormalized model ModelBuilder builds for display/matching.
//      ChangesetApplier needs the raw GedDocument/GedRecord parse tree
//      (from Ged70Parser) to preserve exact structure and formatting for
//      its byte-stable round-trip check; there is no conversion between
//      the two.
//   2. ChangesetApplier holds one exclusive file handle (FileShare.None)
//      from its initial read straight through the verified write, so
//      nothing else can touch the file in between. Fetching a snapshot
//      first and writing separately later would reopen exactly the
//      time-of-check-to-time-of-use gap that locking exists to close.
// A successful apply_changeset write is picked up by DocumentSession's own
// staleness check (and the file watcher) on the next tool call, exactly
// like any other external edit.
// ---------------------------------------------------------------------------

static class ChangesetToolSupport
{
    /// <summary>
    /// Runs a changeset against the document. A <paramref name="policy"/> violation always fails the
    /// result; a mutating run is refused before anything is applied, while a dry run still reports
    /// the document's own errors beside the violations so the author sees every problem at once.
    /// </summary>
    public static CallToolResult Execute(
        string absoluteGedcomPath, string changesetPath, string items, bool dryRun,
        CancellationToken cancellationToken = default, ChangesetPolicy? policy = null,
        IReadOnlyList<PointsCard>? cards = null)
    {
        if (!ChangesetRequest.TryLoad(changesetPath, items, out var request, out var error))
            return error!;

        var violations = policy?.Check(request!.Json, request.ItemNumbers.ToHashSet()) ?? [];
        if (violations.Count > 0 && !dryRun)
            return CallToolResults.Success(new ChangesetResult(false, [], [.. violations], [], [], []), CallToolResults.JsonOptions);

        ApplyResult result;
        try
        {
            result = ChangesetApplier.Run(absoluteGedcomPath, request!.Changeset, request.ItemNumbers, dryRun,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CallToolResults.Error($"Could not open '{absoluteGedcomPath}': {ex.Message}");
        }

        var mapped = Map(result, violations);
        if (cards is not null) mapped = mapped with { PointsPreview = CardScorer.Preview(cards, result.Changes) };
        return CallToolResults.Success(mapped, CallToolResults.JsonOptions);
    }

    static ChangesetResult Map(ApplyResult result, IReadOnlyList<string> violations) =>
        new(result.Success && violations.Count == 0, result.Log, [.. violations, .. result.Errors],
            result.Deltas, result.MintedXrefs, result.CopiedSources);
}

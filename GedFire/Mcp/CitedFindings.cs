using System.Text.RegularExpressions;
using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The other findings a finding's notes and facts cite, checked with it so a
// cite of another finding of the batch (or the file beside it) is judged as
// draft_changeset judges it. Only the checked finding is drafted; the cited
// ones lend their records and are checked on their own.
// ---------------------------------------------------------------------------

public sealed partial class CitedFindings
{
    /// <param name="others">Findings the cites may name; those they do not name are left out.</param>
    public CitedFindings(DraftFinding finding, IReadOnlyList<DraftFinding> others)
    {
        Finding = finding;
        var ids = finding.CitedIds.ToHashSet(StringComparer.Ordinal);
        Cited = [.. others.Where(o => ids.Contains(o.Id)).DistinctBy(o => o.Id)];
    }

    /// <summary>The finding checked, drafted alone as findings[0].</summary>
    public DraftFinding Finding { get; }

    /// <summary>Each finding it cites, which is not drafted.</summary>
    public IReadOnlyList<DraftFinding> Cited { get; }

    /// <summary>The errors, with findings[0], the checked finding, relabelled <paramref name="label"/>.</summary>
    public static IEnumerable<string> Relabel(IEnumerable<string> errors, string label) =>
        errors.Select(e => FindingIndex().Replace(e, label));

    [GeneratedRegex(@"findings\[0\]")]
    private static partial Regex FindingIndex();
}

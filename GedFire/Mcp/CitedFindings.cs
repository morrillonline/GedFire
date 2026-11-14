using System.Text.RegularExpressions;
using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The other findings a finding's notes cite, checked with it so a note that
// cites another finding of the batch (or the file beside it) is judged as
// draft_changeset judges it. The checked finding is always findings[0]; the
// cited ones follow it.
// ---------------------------------------------------------------------------

public sealed partial class CitedFindings
{
    readonly DraftFinding _finding;
    readonly IReadOnlyList<DraftFinding> _cited;

    /// <param name="others">Findings the notes may cite; those they do not cite are left out.</param>
    public CitedFindings(DraftFinding finding, IReadOnlyList<DraftFinding> others)
    {
        _finding = finding;
        var ids = finding.Notes.SelectMany(n => n.Cites).Select(c => c.Finding).OfType<string>()
            .Where(id => id != finding.Id).ToHashSet(StringComparer.Ordinal);
        _cited = [.. others.Where(o => ids.Contains(o.Id)).DistinctBy(o => o.Id)];
    }

    /// <summary>The finding first, then each finding it cites.</summary>
    public IReadOnlyList<DraftFinding> Findings => [_finding, .. _cited];

    /// <summary>
    /// The errors that concern the checked finding, with findings[0] relabelled <paramref name="label"/>. An error that names
    /// only a cited finding is that finding's own, reported when it is checked.
    /// </summary>
    public IEnumerable<string> OwnErrors(IEnumerable<string> errors, string label)
    {
        IReadOnlyList<string> labels = [label, .. _cited.Select(c => $"finding \"{c.Id}\"")];
        return errors.Where(e => FindingIndex().Matches(e) is var named && (named.Count == 0 || named.Any(m => m.Groups[1].Value == "0")))
            .Select(e => FindingIndex().Replace(e, m => int.Parse(m.Groups[1].Value) is var i && i < labels.Count ? labels[i] : m.Value));
    }

    [GeneratedRegex(@"findings\[(\d+)\]")]
    private static partial Regex FindingIndex();
}

using System.Text.RegularExpressions;
using GedCore.Policy;

namespace GedCore.Packages;

/// <summary>
/// A verification note saying a fact rests on one record alone understates the evidence when the package cites that
/// fact, on that record, from two or more distinct records (source and page).
/// </summary>
internal static partial class VerificationNoteBasisRule
{
    [GeneratedRegex(@"\balone and (?:has|have) not yet been confirmed by a second source", RegexOptions.IgnoreCase)]
    private static partial Regex RestsOnOneAlone();

    public static IEnumerable<string> Check(PackageContents package, string notePrefix)
    {
        foreach (var note in package.Notes.Where(n => n.Text.Contains(notePrefix, StringComparison.OrdinalIgnoreCase) && RestsOnOneAlone().IsMatch(n.Text)))
        {
            string claim = note.Text[..RestsOnOneAlone().Match(note.Text).Index];
            foreach (string tag in VerificationNoteText.Labels.Keys.Where(t => VerificationNoteText.Mentions(claim, t)))
            {
                var records = DistinctRecords(package.Citations.Where(c => c.Record == note.Record && c.Fact == tag));
                if (records.Count < 2) continue;
                string xref = PackageContents.Xref(note.Record);
                string label = VerificationNoteText.Label(tag);
                yield return $"{note.Where} writes a note on {xref} saying the {label} rests on one record alone, but the package " +
                             $"cites the {label} from {records.Count} records ({string.Join("; ", records.Select(r => $"{r.Source}, page \"{r.Page}\", in {r.Where}"))}); " +
                             "put the findings for that fact in one item, so its note names every record, or draft the later " +
                             "changeset after the earlier one is applied, when the drafter rewrites the note to name both";
            }
        }
    }

    // One citation per record cited: the same source and page count once, wherever they are cited.
    static List<PackageCitation> DistinctRecords(IEnumerable<PackageCitation> citations)
    {
        var distinct = new List<PackageCitation>();
        foreach (var citation in citations)
            if (!distinct.Any(d => d.Page == citation.Page && d.Source.SameAs(citation.Source)))
                distinct.Add(citation);
        return distinct;
    }
}

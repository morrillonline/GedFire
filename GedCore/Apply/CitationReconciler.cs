namespace GedCore.Apply;

/// <summary>
/// Brings the citations on one structure (a fact, a note, or a FAM) in line
/// with the citations a request states, one source at a time. The request says
/// what each citation should read; this works out which existing citation each
/// one refers to, so nobody spells out what to remove or add.
///
/// For one source, the stated citations are matched to that source's existing
/// citations on the structure:
///   1. a stated citation whose page equals an existing citation's page (or
///      that names no page, against an existing citation with none) updates
///      that citation's other fields;
///   2. what is left over is paired in order and updated in place only when
///      the counts are equal, which is how a wrong page is corrected;
///   3. with nothing left to pair against, each leftover becomes a new citation;
///   4. any other combination is ambiguous and is rejected.
/// Nothing is ever removed here; removal is deleteCitation's job.
/// </summary>
internal static class CitationReconciler
{
    public static List<string> Upsert(ApplyState state, GedRecord structure, IReadOnlyList<Citation> citations)
    {
        var changes = new List<string>();
        foreach (var group in citations.GroupBy(c => c.Source))
        {
            var swapped = group.Select(c => c.SwappedFrom).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var existing = Resolve.CitationsOnStructure(structure, group.Key, swapped);
            foreach (var (node, citation) in Plan(group.Key, existing, [.. group]))
            {
                string? change = node is null
                    ? NodeBuilder.AddCitation(state, structure, citation)
                    : NodeBuilder.UpdateCitation(state, structure, node, citation);
                if (change is not null) changes.Add(change);
            }
        }
        return changes;
    }

    static List<(GedRecord? Node, Citation Citation)> Plan(
        string source, IReadOnlyList<GedRecord> existing, IReadOnlyList<Citation> stated)
    {
        var plan = new List<(GedRecord? Node, Citation Citation)>();
        var unmatched = existing.ToList();
        var leftover = new List<Citation>();

        foreach (var citation in stated)
        {
            var match = unmatched.FirstOrDefault(node => SamePage(node, citation));
            if (match is null) { leftover.Add(citation); continue; }
            unmatched.Remove(match);
            plan.Add((match, citation));
        }

        if (leftover.Count > 0 && unmatched.Count == 0)
            plan.AddRange(leftover.Select(c => ((GedRecord?)null, c)));
        else if (leftover.Count > 0 && leftover.Count == unmatched.Count)
            plan.AddRange(leftover.Select((c, i) => ((GedRecord?)unmatched[i], c)));
        else if (leftover.Count > 0)
            throw new InvalidOperationException(
                $"citation {source}: the structure already cites it {existing.Count} time(s) " +
                $"(pages: {DescribePages(existing)}) and the request states {stated.Count} " +
                $"that do not line up — state every citation of {source} wanted on this structure " +
                "with its page, or use deleteCitation to remove one");

        return plan;
    }

    static bool SamePage(GedRecord node, Citation citation) =>
        citation.Page is null ? PageOf(node) is null : PageOf(node) == citation.Page;

    static string? PageOf(GedRecord node) => node.FirstChild("PAGE")?.Value;

    internal static string DescribePages(IEnumerable<GedRecord> nodes) =>
        string.Join(", ", nodes.Select(n => PageOf(n) is string p ? $"\"{p}\"" : "no page"));
}

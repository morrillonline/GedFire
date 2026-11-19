namespace GedCore.Apply;

/// <summary>
/// Brings the citations on one structure (a fact, a note, or a FAM) in line
/// with the citations a request states, one source at a time. One source may
/// be cited on many pages of one structure, each page its own citation:
///   1. a stated citation whose page equals an existing citation's page of
///      that source (or that names no page, against one with none) updates
///      that citation's other fields;
///   2. a stated citation with <see cref="Citation.ReplacesPage"/> corrects
///      the existing citation on that page: its page and other fields are
///      rewritten; no existing citation on that page is an error;
///   3. any other stated citation is added beside the existing ones.
/// A page is never changed unless the request names the page it replaces.
/// Nothing is ever removed here; removal is deleteCitation's job.
///
/// Commentary-only citations (<see cref="GedRecord.VoidPointer"/>) have no
/// page to match on: one whose commentary the structure already carries is a
/// no-op, and any other is added beside the rest.
/// </summary>
internal static class CitationReconciler
{
    public static List<string> Upsert(ApplyState state, GedRecord structure, IReadOnlyList<Citation> citations)
    {
        var changes = new List<string>();
        foreach (var commentary in citations.Where(c => c.IsCommentaryOnly))
            if (CommentaryCitation(structure, commentary.Commentary!) is null)
                changes.Add(NodeBuilder.AddCitation(state, structure, commentary));
        foreach (var group in citations.Where(c => !c.IsCommentaryOnly).GroupBy(c => c.Source))
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

    static List<(GedRecord? Node, Citation Citation)> Plan(string source, IReadOnlyList<GedRecord> existing, IReadOnlyList<Citation> stated)
    {
        var plan = new List<(GedRecord? Node, Citation Citation)>();
        var unmatched = existing.ToList();
        foreach (var citation in stated)
        {
            var match = citation.ReplacesPage is { } replaced
                ? unmatched.FirstOrDefault(node => PageOf(node) == replaced)
                  ?? throw new InvalidOperationException(
                      $"citation {source}: replacesPage \"{replaced}\" names no citation of {source} on the structure " +
                      $"(pages: {DescribePages(existing)})")
                : unmatched.FirstOrDefault(node => SamePage(node, citation));
            if (match is not null) unmatched.Remove(match);
            plan.Add((match, citation));
        }
        return plan;
    }

    static bool SamePage(GedRecord node, Citation citation) =>
        citation.Page is null ? PageOf(node) is null : PageOf(node) == citation.Page;

    /// <summary>The structure's commentary-only citation whose commentary reads <paramref name="commentary"/>, or null.</summary>
    internal static GedRecord? CommentaryCitation(GedRecord structure, string commentary)
    {
        string text = NodeBuilder.NormalizeText(commentary);
        return structure.ChildrenByTag("SOUR")
            .FirstOrDefault(s => s.IsVoidPointer && s.FirstChild("NOTE")?.FullValue() == text);
    }

    static string? PageOf(GedRecord node) => node.FirstChild("PAGE")?.Value;

    internal static string DescribePages(IEnumerable<GedRecord> nodes) =>
        string.Join(", ", nodes.Select(n => PageOf(n) is string p ? $"\"{p}\"" : "no page"));
}

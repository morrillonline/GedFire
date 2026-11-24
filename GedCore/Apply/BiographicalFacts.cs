namespace GedCore.Apply;

/// <summary>A cited fact a changeset added, improved or corrected on a person, or on a family the person is a partner in.</summary>
public sealed record BiographicalFact(string Record, string Tag)
{
    public override string ToString() => $"{Tag} {Record}";
}

/// <summary>
/// The facts that earn biography credit: every fact a changeset writes except the person's identity (NAME, SEX) and the
/// birth, death and marriage the Enrich person card pays for. A fact earns it when, after the changeset, it holds a
/// value, date, place, detail or cited source and page that no instance of its tag on the record held before, and it
/// cites a source.
/// </summary>
internal static class BiographicalFacts
{
    static readonly HashSet<string> Excluded = new(StringComparer.Ordinal) { "NAME", "SEX", "BIRT", "DEAT", "MARR" };

    static readonly HashSet<string> Tags = new(CreateOrUpdateVitalOp.Facts.Where(t => !Excluded.Contains(t)), StringComparer.Ordinal);

    /// <summary>The people each written fact credits: the person it is on, or both partners of the family it is on.</summary>
    public static IEnumerable<(string Person, BiographicalFact Fact)> Find(
        GedDocument before, IEnumerable<GedRecord> touchedRoots, Func<string, string> canonicalSource)
    {
        foreach (var root in touchedRoots.Where(r => r.Xref is not null && r.Tag is "INDI" or "FAM"))
        {
            before.ByXref.TryGetValue(root.Xref!, out var prior);
            var held = prior is null ? [] : Fingerprints(prior, canonicalSource).ToHashSet(StringComparer.Ordinal);
            var written = Facts(root)
                .Where(f => CitesASource(f) && !held.Contains(Fingerprint(f, canonicalSource)))
                .Select(f => new BiographicalFact(root.Xref!, f.Tag))
                .Distinct()
                .ToList();
            if (written.Count == 0) continue;

            var people = root.Tag == "INDI"
                ? [root.Xref!]
                : root.Children.Where(c => c.Tag is "HUSB" or "WIFE" && c.IsPointerValue && !c.IsVoidPointer).Select(c => c.Value);
            foreach (string person in people)
                foreach (var fact in written)
                    yield return (person, fact);
        }
    }

    static IEnumerable<GedRecord> Facts(GedRecord record) => record.Children.Where(c => Tags.Contains(c.Tag));

    static IEnumerable<string> Fingerprints(GedRecord record, Func<string, string> canonicalSource) =>
        Facts(record).Select(f => Fingerprint(f, canonicalSource));

    static bool CitesASource(GedRecord fact) => fact.ChildrenByTag("SOUR").Any(s => s.IsPointerValue && !s.IsVoidPointer);

    // What the fact says: its tag and payload, each substructure but its citations as written, and the sources and pages it
    // cites in any order. A citation's extract, grade and commentary do not make the fact new.
    static string Fingerprint(GedRecord fact, Func<string, string> canonicalSource)
    {
        var parts = new List<string> { fact.Tag, fact.FullValue().Trim() };
        parts.AddRange(fact.Children.Where(c => c.Tag is not ("SOUR" or "OBJE")).Select(Substructure));
        parts.AddRange(fact.ChildrenByTag("SOUR")
            .Where(s => s.IsPointerValue && !s.IsVoidPointer)
            .Select(s => new BiographyCitation(canonicalSource(s.Value), s.FirstChild("PAGE")?.Value))
            .Distinct()
            .Select(c => $"SOUR {c.Source} {string.Join(' ', (c.Page ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant()}")
            .Order(StringComparer.Ordinal));
        return string.Join("\u001f", parts);
    }

    static string Substructure(GedRecord node) =>
        $"{node.Tag} {node.FullValue().Trim()}" + string.Concat(node.Children.Select(c => "\u001e" + Substructure(c)));
}

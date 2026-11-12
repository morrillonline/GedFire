namespace GedCore.Apply;

/// <summary>
/// A citation as biography credit counts it: its source and record locator (<c>SOUR</c> + <c>PAGE</c>).
/// Extract text, grade and position do not make a citation new, nor do case or spacing in the page.
/// </summary>
public sealed record BiographyCitation(string Source, string? Page)
{
    public bool Equals(BiographyCitation? other) =>
        other is not null && Source == other.Source && string.Equals(Fold(Page), Fold(other.Page), StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Source, Fold(Page)?.ToUpperInvariant());

    public override string ToString() => Page is null ? Source : $"{Source} \"{Page}\"";

    static string? Fold(string? page) =>
        page is null ? null : string.Join(' ', page.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>A person whose biography notes gained citations they did not carry before the changeset.</summary>
public sealed record BiographyWrite(string Record, IReadOnlyList<BiographyCitation> NewCitations);

/// <summary>
/// A person's biographical notes, recognized by standard GEDCOM 7 structure alone: a person-level NOTE
/// with prose and at least one source citation, the narrative-biography form the generator renders as a
/// cited paragraph. Nothing is written to mark them, and the note's text is never read to decide.
/// </summary>
public static class BiographyNotes
{
    /// <summary>The person's notes that hold prose and cite a source.</summary>
    public static IEnumerable<GedRecord> On(GedRecord person) =>
        person.ChildrenByTag("NOTE").Where(n => n.FullValue().Trim().Length > 0 && CitationsOf(n).Any());

    public static IEnumerable<BiographyCitation> CitationsOf(GedRecord note) =>
        note.ChildrenByTag("SOUR")
            .Where(s => s.IsPointerValue && s.Value != "@VOID@")
            .Select(s => new BiographyCitation(s.Value, s.FirstChild("PAGE")?.Value));
}

/// <summary>
/// Finds the biography credit a run earned: per person, the citations biography note ops added that none of
/// the person's biography notes carried before the run and that a biography note still carries after it.
/// A source copied for the run counts as the source it stands in for.
/// </summary>
internal static class BiographyCredit
{
    public static IReadOnlyList<BiographyWrite> Find(GedDocument before, GedDocument after, ApplyState state)
    {
        var originals = state.CopiedSources
            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        BiographyCitation Canonical(BiographyCitation c) =>
            originals.TryGetValue(c.Source, out var original) ? c with { Source = original } : c;

        var writes = new List<BiographyWrite>();
        foreach (var (person, added) in state.BiographyCitationsAdded)
        {
            var held = Citations(before, person, Canonical);
            var kept = Citations(after, person, Canonical);
            var fresh = added.Select(Canonical).Distinct().Where(c => kept.Contains(c) && !held.Contains(c)).ToList();
            if (fresh.Count > 0) writes.Add(new BiographyWrite(person, fresh));
        }
        return writes;
    }

    static HashSet<BiographyCitation> Citations(GedDocument doc, string person, Func<BiographyCitation, BiographyCitation> canonical) =>
        doc.ByXref.TryGetValue(person, out var record)
            ? BiographyNotes.On(record).SelectMany(BiographyNotes.CitationsOf).Select(canonical).ToHashSet()
            : [];
}

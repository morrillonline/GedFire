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

/// <summary>
/// A person whose biography gained new information. <see cref="NewCitations"/> are citations none of their biography
/// notes carried before the changeset. <see cref="NewText"/> says whether a biography note's text matches none of the
/// person's notes before it (see <see cref="BiographyNotes.NormalizeText"/>). <see cref="Facts"/> are the cited facts
/// it added, improved or corrected beyond birth, death and marriage (see <see cref="BiographicalFacts"/>). Any one earns the credit.
/// </summary>
public sealed record BiographyWrite(string Record, IReadOnlyList<BiographyCitation> NewCitations, bool NewText = false)
{
    public IReadOnlyList<BiographicalFact> Facts { get; init; } = [];
}

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

    /// <summary>
    /// A note's text as biography credit compares it: case folded, punctuation dropped, and whitespace collapsed, so a
    /// replay that differs only in those is the same text. Rewording is not judged; a reviewer decides that.
    /// </summary>
    public static string NormalizeText(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant())
            kept.Append(char.IsLetterOrDigit(c) ? c : char.IsWhiteSpace(c) ? ' ' : '\0');
        return string.Join(' ', kept.ToString().Replace("\0", "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Finds the biography credit a run earned, per person: the citations biography note ops added that none of the person's
/// biography notes carried before the run and that a biography note still carries after it; a biography note the
/// run wrote whose text matches none of the person's notes before the run and that is still on the person after it;
/// and the cited facts beyond birth, death and marriage it added, improved or corrected on the person or their family.
/// A source copied for the run counts as the source it stands in for.
/// </summary>
internal static class BiographyCredit
{
    public static IReadOnlyList<BiographyWrite> Find(GedDocument before, GedDocument after, ApplyState state)
    {
        var originals = state.CopiedSources
            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        string CanonicalSource(string source) => originals.GetValueOrDefault(source, source);
        BiographyCitation Canonical(BiographyCitation c) => c with { Source = CanonicalSource(c.Source) };

        var facts = BiographicalFacts.Find(before, state.TouchedRoots, CanonicalSource)
            .GroupBy(f => f.Person, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BiographicalFact>)[.. g.Select(f => f.Fact).Distinct()], StringComparer.Ordinal);

        var writes = new List<BiographyWrite>();
        foreach (string person in state.BiographyCitationsAdded.Keys.Union(state.BiographyTextsWritten.Keys).Union(facts.Keys))
        {
            var added = state.BiographyCitationsAdded.GetValueOrDefault(person, []);
            var held = Citations(before, person, Canonical);
            var kept = Citations(after, person, Canonical);
            var fresh = added.Select(Canonical).Distinct().Where(c => kept.Contains(c) && !held.Contains(c)).ToList();
            bool newText = NewText(before, after, person, state.BiographyTextsWritten.GetValueOrDefault(person, []));
            var written = facts.GetValueOrDefault(person, []);
            if (fresh.Count > 0 || newText || written.Count > 0)
                writes.Add(new BiographyWrite(person, fresh, newText) { Facts = written });
        }
        return writes;
    }

    static HashSet<BiographyCitation> Citations(GedDocument doc, string person, Func<BiographyCitation, BiographyCitation> canonical) =>
        doc.ByXref.TryGetValue(person, out var record)
            ? BiographyNotes.On(record).SelectMany(BiographyNotes.CitationsOf).Select(canonical).ToHashSet()
            : [];

    // Every note the person held counts, cited or not: information already captured in any note is not new.
    static bool NewText(GedDocument before, GedDocument after, string person, IReadOnlyList<string> written)
    {
        var held = NoteTexts(before, person);
        var kept = NoteTexts(after, person);
        return written.Select(BiographyNotes.NormalizeText).Any(text => text.Length > 0 && !held.Contains(text) && kept.Contains(text));
    }

    static HashSet<string> NoteTexts(GedDocument doc, string person) =>
        doc.ByXref.TryGetValue(person, out var record)
            ? record.ChildrenByTag("NOTE").Select(n => BiographyNotes.NormalizeText(n.FullValue())).ToHashSet(StringComparer.Ordinal)
            : [];
}

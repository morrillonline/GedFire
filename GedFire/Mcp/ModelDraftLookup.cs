using GedCore.Drafting;
using GedFire.Gen;

namespace GedFire.Mcp;

/// <summary>Answers a draft's questions about the document from the server's current snapshot.</summary>
public sealed class ModelDraftLookup(GedModel model) : IDraftLookup
{
    public string? FindSource(string title, string? author) =>
        model.Sources.Values
            .Where(s => SourceTitles.SameCollection(s.Title, title) && (author is null || Same(s.Author, author)))
            .OrderBy(s => s.Xref, StringComparer.Ordinal)
            .Select(s => s.Xref)
            .FirstOrDefault();

    public string? PersonName(string xref) =>
        model.Individuals.TryGetValue(xref, out var person) && person.Fullname.Length > 0 ? person.Fullname : null;

    public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) =>
        [.. EventsOf(record, tag).Select(e => new ExistingFact(NullIfEmpty(e.Date), NullIfEmpty(e.Place), CitationsOf(e)))];

    static ExistingCitation[] CitationsOf(GedEvent fact) =>
        [.. fact.Sources
            .Where(s => s.GlobalSource is not null)
            .Select(s => new ExistingCitation(
                s.GlobalSource!.Xref, NullIfEmpty(s.Page), NullIfEmpty(s.DataText), s.Quay, NullIfEmpty(s.GlobalSource.Title)))];

    public IReadOnlyList<string> NotesOn(string record) =>
        model.Individuals.TryGetValue(record, out var person) ? [.. person.NarrativeNotes.Select(n => n.Text)] : [];

    public bool? IsChildOf(string child, string family) =>
        model.Families.TryGetValue(family, out var fam) && model.Individuals.ContainsKey(child)
            ? fam.Children.Any(c => c.Xref == child)
            : null;

    IEnumerable<GedEvent> EventsOf(string record, string tag)
    {
        if (model.Families.TryGetValue(record, out var family))
            return tag == "MARR" && family.Marriage is { } marriage ? [marriage] : [];
        if (!model.Individuals.TryGetValue(record, out var person)) return [];

        return tag switch
        {
            "BIRT" => Single(person.Birth),
            "DEAT" => Single(person.Death),
            "WILL" => Single(person.Will),
            "PROB" => Single(person.Probate),
            "CENS" => person.Census,
            _ => person.OtherEvents.Where(e => e.Tag == tag),
        };
    }

    static IEnumerable<GedEvent> Single(GedEvent? e) => e is null ? [] : [e];

    static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    static bool Same(string recorded, string wanted) =>
        string.Equals(recorded.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
}

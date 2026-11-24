using GedCore.Apply;
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

    public ExistingSource? SourceRecord(string xref) =>
        model.Sources.TryGetValue(xref, out var source) ? new ExistingSource(source.Title, NullIfEmpty(source.Author)) : null;

    public string? PersonName(string xref) =>
        model.Individuals.TryGetValue(xref, out var person) && person.Fullname.Length > 0 ? person.Fullname : null;

    public string? PersonSurname(string xref) =>
        model.Individuals.TryGetValue(xref, out var person) && person.Fullname.Length > 0 ? person.LastNameRaw : null;

    public bool HasPerson(string xref) => model.Individuals.ContainsKey(xref);

    public bool? HasRecord(string xref) =>
        model.Individuals.ContainsKey(xref) || model.Families.ContainsKey(xref) || model.Sources.ContainsKey(xref);

    public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) =>
        [.. EventsOf(record, tag).Select(e => new ExistingFact(NullIfEmpty(e.Date), NullIfEmpty(e.Place), CitationsOf(e)))];

    static ExistingCitation[] CitationsOf(GedEvent fact) =>
        [.. fact.Sources
            .Where(s => s.GlobalSource is not null)
            .Select(s => new ExistingCitation(
                s.GlobalSource!.Xref, NullIfEmpty(s.Page), NullIfEmpty(s.DataText), s.Quay, NullIfEmpty(s.GlobalSource.Title)))];

    public IReadOnlyList<string> NotesOn(string record) =>
        model.Individuals.TryGetValue(record, out var person) ? [.. person.NarrativeNotes.Select(n => n.Text)]
        : model.Families.TryGetValue(record, out var family) ? [.. family.NarrativeNotes.Select(n => n.Text)]
        : [];

    public bool? IsChildOf(string child, string family) =>
        model.Families.TryGetValue(family, out var fam) && model.Individuals.ContainsKey(child)
            ? fam.Children.Any(c => c.Xref == child)
            : null;

    public IReadOnlyList<string> FamiliesOfCouple(string partner, string otherPartner) =>
        model.Individuals.TryGetValue(partner, out var person)
            ? [.. person.FamSpouse.Where(f => f.SpouseOf(person)?.Xref == otherPartner).Select(f => f.Xref).Distinct().Order(StringComparer.Ordinal)]
            : [];

    public Relative? Person(string xref) => model.Individuals.TryGetValue(xref, out var person) ? AsRelative(person) : null;

    public IReadOnlyList<string> FamiliesAsChild(string person) =>
        model.Individuals.TryGetValue(person, out var indi) && indi.FamChild is { } family ? [family.Xref] : [];

    public IReadOnlyList<string> FamiliesAsPartner(string person) =>
        model.Individuals.TryGetValue(person, out var indi) ? [.. indi.FamSpouse.Select(f => f.Xref)] : [];

    public Relative? Partner(string family, string tag) =>
        model.Families.TryGetValue(family, out var fam) && (tag == "HUSB" ? fam.Husband : fam.Wife) is { } partner
            ? AsRelative(partner)
            : null;

    public IReadOnlyList<Relative> Children(string family) =>
        model.Families.TryGetValue(family, out var fam) ? [.. fam.Children.Select(AsRelative)] : [];

    // The model keeps the name in parts; rejoined in GEDCOM form so the surname stays marked.
    static Relative AsRelative(GedIndividual person) =>
        new(person.Xref, $"{string.Join(' ', new[] { person.FirstName, person.MiddleName }.Where(p => p.Length > 0))} /{person.LastNameRaw}/");

    IEnumerable<GedEvent> EventsOf(string record, string tag)
    {
        if (model.Families.TryGetValue(record, out var family))
            return new[] { family.Marriage, family.Divorce }.OfType<GedEvent>()
                .Concat(family.OtherEvents).Where(e => e.Tag == tag);
        if (!model.Individuals.TryGetValue(record, out var person)) return [];

        return tag switch
        {
            "BIRT" => Single(person.Birth),
            "DEAT" => Single(person.Death),
            "WILL" => Single(person.Will),
            "PROB" => Single(person.Probate),
            _ => person.Census.Concat(person.OtherEvents).Concat(person.Attributes).Where(e => e.Tag == tag),
        };
    }

    static IEnumerable<GedEvent> Single(GedEvent? e) => e is null ? [] : [e];

    static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    static bool Same(string recorded, string wanted) =>
        string.Equals(recorded.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
}

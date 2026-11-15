using GedCore.Drafting;

namespace GedCore.Policy;

/// <summary>
/// What a parsed document already says about a record's facts and notes, read the first time a policy rule asks:
/// most changesets carry everything the policy needs, so the document is often never read.
/// </summary>
public sealed class DocumentHistory(Func<GedDocument?> read) : IRecordHistory
{
    readonly Lazy<GedDocument?> _doc = new(read);

    public DocumentHistory(GedDocument doc) : this(() => doc) { }

    public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) =>
        Record(record) is { } rec
            ? [.. rec.ChildrenByTag(tag).Select(fact => new ExistingFact(
                fact.FirstChild("DATE")?.Value, fact.FirstChild("PLAC")?.Value, [.. CitationsOn(fact)]))]
            : [];

    /// <summary>The text of each note on the record: its own NOTEs and the shared notes it points to.</summary>
    public IReadOnlyList<string> NotesOn(string record) =>
        Record(record) is { } rec
            ? [.. rec.Children
                .Where(c => c.Tag is "NOTE" or "SNOTE")
                .Select(c => c.IsPointerValue ? Record(c.Value)?.FullValue() : c.FullValue())
                .OfType<string>()]
            : [];

    GedRecord? Record(string xref) => _doc.Value is { } doc && doc.ByXref.TryGetValue(xref, out var rec) ? rec : null;

    IEnumerable<ExistingCitation> CitationsOn(GedRecord fact) =>
        fact.ChildrenByTag("SOUR").Where(s => s.IsPointerValue).Select(s => new ExistingCitation(
            s.Value,
            s.FirstChild("PAGE")?.Value,
            s.FirstChild("DATA")?.FirstChild("TEXT")?.FullValue(),
            int.TryParse(s.FirstChild("QUAY")?.Value, out int quay) ? quay : null,
            Record(s.Value)?.FirstChild("TITL")?.FullValue()));
}

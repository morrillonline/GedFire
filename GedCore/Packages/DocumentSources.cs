namespace GedCore.Packages;

/// <summary>The sources a parsed GEDCOM document holds: each title, and the web address its note or publication gives.</summary>
public sealed class DocumentSources(Func<GedDocument> read) : ISourceRecords
{
    readonly Lazy<GedDocument> _doc = new(read);

    public SourceDescription? Source(string xref)
    {
        if (!_doc.Value.ByXref.TryGetValue(xref, out var source) || source.Tag != "SOUR") return null;
        string? note = source.ChildrenByTag("NOTE").Select(n => n.FullValue()).FirstOrDefault(n => SourceNoteUrl.Find(n) is not null);
        return new SourceDescription(
            source.FirstChild("TITL")?.FullValue(),
            SourceNoteUrl.Find(note) ?? SourceNoteUrl.Find(source.FirstChild("PUBL")?.FullValue()));
    }
}

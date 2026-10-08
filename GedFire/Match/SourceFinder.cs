using GedCore;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Finds the existing sources that match every supplied criterion: a
// case-insensitive substring of the title, of the author, or of the web
// address recorded in the source's note.
// ---------------------------------------------------------------------------

public sealed record SourceQuery(string? Title = null, string? Author = null, string? Url = null)
{
    public bool IsEmpty => Title is null && Author is null && Url is null;
}

public sealed record FoundSource(string Xref, string? Title, string? Author, string? Url);

public static class SourceFinder
{
    public static IReadOnlyList<FoundSource> Find(GedModel model, SourceQuery query)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(query);

        return [.. model.Sources.Values
            .Select(ToFoundSource)
            .Where(s => Contains(s.Title, query.Title) && Contains(s.Author, query.Author) && Contains(s.Url, query.Url))
            .OrderBy(s => s.Title ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Xref, StringComparer.Ordinal)];
    }

    static FoundSource ToFoundSource(GedSourceRecord source)
    {
        string note = FtmCitationText.ParseSourceNote(source.NoteRaw, out _, out _);
        return new FoundSource(
            source.Xref,
            NullIfEmpty(source.Title),
            NullIfEmpty(source.Author),
            SourceNoteUrl.Find(note) ?? SourceNoteUrl.Find(source.Publication));
    }

    static bool Contains(string? value, string? needle) =>
        needle is null || (value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

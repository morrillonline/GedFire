using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// A source citation attached to a fact: pointer to a SOUR record plus the
/// locator, verbatim extract, and QUAY assessment. Shared by every
/// fact-asserting op in the changeset dialect.
/// </summary>
public sealed record Citation(string Source, string? Page, string? DataText, int? Quay)
{
    /// <summary>Accepts both the singular "citation" and plural "citations" JSON forms.</summary>
    internal static IReadOnlyList<Citation> ReadAll(JsonElement el)
    {
        if (el.TryGetProperty("citations", out var many))
            return [.. many.EnumerateArray().Select(ReadOne)];
        if (el.TryGetProperty("citation", out var one))
            return [ReadOne(one)];
        return [];
    }

    internal static Citation ReadOne(JsonElement el) => new(
        el.GetProperty("source").GetString()!,
        JsonRead.Str(el, "page"),
        JsonRead.Str(el, "dataText"),
        el.TryGetProperty("quay", out var q) && q.ValueKind == JsonValueKind.Number
            ? q.GetInt32() : null);
}

/// <summary>
/// A fact payload: a bare string means a text payload (NAME); an object
/// carries date and/or place (events).
/// </summary>
public sealed record EventValue(string? Text, string? Date, string? Place)
{
    internal static EventValue? Read(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => new EventValue(v.GetString(), null, null),
            JsonValueKind.Object => new EventValue(
                null, JsonRead.Str(v, "date"), JsonRead.Str(v, "place")),
            _ => throw new JsonException($"unsupported value kind for '{name}'"),
        };
    }
}

/// <summary>
/// Selector that picks WHICH same-tag fact instance an op targets when a
/// record carries several (two CENS, two MARR). It is only a selector — the
/// dialect has no old-value guard; CreateOrUpdate replaces unconditionally
/// and the dry-run log is the review artifact.
/// </summary>
public sealed record FactMatch(string? Text, string? Date, string? Place)
{
    internal static FactMatch? Read(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => new FactMatch(v.GetString(), null, null),
            JsonValueKind.Object => new FactMatch(
                null, JsonRead.Str(v, "date"), JsonRead.Str(v, "place")),
            _ => throw new JsonException($"unsupported value kind for '{name}'"),
        };
    }
}

public sealed record SubStructure(string Tag, string Value)
{
    internal static IReadOnlyList<SubStructure> ReadList(JsonElement el, string name) =>
        el.TryGetProperty(name, out var subs)
            ? [.. subs.EnumerateArray().Select(s => new SubStructure(
                    s.GetProperty("tag").GetString()!, s.GetProperty("value").GetString()!))]
            : [];
}

/// <summary>A fact carried inline by a person description (PersonRef).</summary>
public sealed record InlineFact(string Fact, EventValue? Value, IReadOnlyList<Citation> Citations)
{
    internal static InlineFact Read(JsonElement el) => new(
        el.GetProperty("fact").GetString()!,
        EventValue.Read(el, "value"),
        Citation.ReadAll(el));

    internal static IReadOnlyList<InlineFact> ReadList(JsonElement el, string name) =>
        el.TryGetProperty(name, out var facts)
            ? [.. facts.EnumerateArray().Select(Read)]
            : [];
}

/// <summary>
/// A person named by a relationship op: either a link to an existing INDI
/// (bare xref string, or object with only "xref") or an inline description
/// of a new person to create ("name" present; explicit new xref required so
/// changesets stay reviewable and xref numbering stays conventional).
/// </summary>
public sealed record PersonRef(string Xref, string? Name, string? Sex, IReadOnlyList<InlineFact> Facts)
{
    public bool IsInline => Name is not null;

    internal static PersonRef? Read(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => new PersonRef(v.GetString()!, null, null, []),
            JsonValueKind.Object => new PersonRef(
                v.GetProperty("xref").GetString()!,
                JsonRead.Str(v, "name"),
                JsonRead.Str(v, "sex"),
                InlineFact.ReadList(v, "facts")),
            _ => throw new JsonException($"unsupported value kind for '{name}'"),
        };
    }
}

/// <summary>One file of a Media object: payload path plus MIME type, optional medium, optional per-file title.</summary>
public sealed record MediaFileRequest(string Path, string MediaType, string? Medium, string? Title)
{
    internal static MediaFileRequest Read(JsonElement el) => new(
        NormalizePath(JsonRead.Req(el, "path", "createOrUpdateMedia")),
        JsonRead.Req(el, "mediaType", "createOrUpdateMedia"),
        JsonRead.Str(el, "medium"),
        JsonRead.Str(el, "title"));

    internal static IReadOnlyList<MediaFileRequest> ReadList(JsonElement el, string name) =>
        el.TryGetProperty(name, out var files)
            ? [.. files.EnumerateArray().Select(Read)]
            : [];

    // GEDCOM 7 §2.12 (File Path) recommends -- without requiring -- that
    // local media files use the "media/" directory prefix, precisely so a
    // FILE payload is self-describing rather than depending on whatever
    // --media-dir a later pack/mcp invocation happens to guess. A composer
    // may omit it for brevity; this is the one place, applied once at parse
    // time so every later comparison (Validate, Apply, FilesMatch,
    // MediaResolve.FindByFiles) sees the same normalized path, that the
    // convention actually gets enforced rather than merely hoped for.
    // Idempotent: a path already carrying the prefix, or an absolute URL
    // (which GEDCOM 7 file paths may also be), passes through unchanged.
    static string NormalizePath(string path) =>
        !MediaPaths.IsAbsoluteUrl(path) && !path.StartsWith("media/", StringComparison.Ordinal)
            ? "media/" + path
            : path;
}

/// <summary>
/// One target a Media link attaches to: exactly one of <see cref="Person"/> /
/// <see cref="Family"/>, an optional link-title override, and whether this
/// link should become the target's portrait (first <c>OBJE</c> link).
/// </summary>
public sealed record MediaAttachRequest(string? Person, string? Family, string? Title, bool Portrait)
{
    internal string Xref => Person ?? Family!;
    internal bool IsPerson => Person is not null;

    internal static MediaAttachRequest Read(JsonElement el) => new(
        JsonRead.Str(el, "person"),
        JsonRead.Str(el, "family"),
        JsonRead.Str(el, "title"),
        JsonRead.Bool(el, "portrait"));

    internal static IReadOnlyList<MediaAttachRequest> ReadList(JsonElement el, string name) =>
        el.TryGetProperty(name, out var arr)
            ? [.. arr.EnumerateArray().Select(Read)]
            : [];
}

internal static class JsonRead
{
    public static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static string Req(JsonElement el, string name, string op) =>
        Str(el, name) ?? throw new JsonException($"{op}: required field '{name}' missing");

    public static bool Bool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

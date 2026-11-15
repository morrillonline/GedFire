using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// A source citation attached to a fact: pointer to a SOUR record plus the
/// locator, verbatim extract, and QUAY assessment. Shared by every
/// fact-asserting op in the changeset dialect.
/// </summary>
public sealed record Citation(string Source, string? Page, string? DataText, int? Quay, string? SwappedFrom = null)
{
    // SwappedFrom is set only by ApplyState.ResolveCitations when a shared
    // source was copied for this item: the original source this citation
    // replaces on the structures it is applied to.
    /// <summary>The citations of the fact an object writes: "citations", or the legacy singular "citation".</summary>
    internal static IReadOnlyList<Citation> ReadAll(JsonElement el)
    {
        if (el.TryGetProperty("citations", out var many))
            return [.. many.EnumerateArray().Select(ReadOne)];
        if (el.TryGetProperty("citation", out var one))
            return [ReadOne(one)];
        return [];
    }

    /// <summary>A relationship op's citations of the family record: "familyCitations", or the legacy op-level citation keys.</summary>
    internal static IReadOnlyList<Citation> ReadFamily(JsonElement el) =>
        el.TryGetProperty("familyCitations", out var family)
            ? [.. family.EnumerateArray().Select(ReadOne)]
            : ReadAll(el);

    internal static Citation ReadOne(JsonElement el) => new(
        el.GetProperty("source").GetString()!,
        JsonRead.Str(el, "page"),
        JsonRead.Str(el, "dataText"),
        el.TryGetProperty("quay", out var q) && q.ValueKind == JsonValueKind.Number
            ? q.GetInt32() : null);
}

/// <summary>
/// A fact payload: the text value (NAME, OCCU…), and the date and place of an
/// event or dated attribute.
/// </summary>
public sealed record EventValue(string? Text, string? Date, string? Place)
{
    /// <summary>
    /// The value, date and place fields of a fact-writing object; null when it gives none. A legacy
    /// object "value" carries the date and place inside it.
    /// </summary>
    internal static EventValue? ReadFact(JsonElement el)
    {
        string? date = JsonRead.Str(el, "date"), place = JsonRead.Str(el, "place");
        if (!el.TryGetProperty("value", out var v) || v.ValueKind == JsonValueKind.Null)
            return date is null && place is null ? null : new EventValue(null, date, place);
        return v.ValueKind switch
        {
            JsonValueKind.String => new EventValue(v.GetString(), date, place),
            JsonValueKind.Object => new EventValue(null, JsonRead.Str(v, "date"), JsonRead.Str(v, "place")),
            _ => throw new JsonException("unsupported value kind for 'value'"),
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
    /// <summary>The "match" object {value?, date?, place?}, or the legacy bare string naming the payload.</summary>
    internal static FactMatch? Read(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => new FactMatch(v.GetString(), null, null),
            JsonValueKind.Object => new FactMatch(
                JsonRead.Str(v, "value"), JsonRead.Str(v, "date"), JsonRead.Str(v, "place")),
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
        EventValue.ReadFact(el),
        Citation.ReadAll(el));

    internal static IReadOnlyList<InlineFact> ReadList(JsonElement el, string name) =>
        el.TryGetProperty(name, out var facts)
            ? [.. facts.EnumerateArray().Select(Read)]
            : [];
}

/// <summary>
/// A person named by a relationship op: either a link to an existing INDI
/// or a description of a new person to create ("name" present; explicit new
/// xref required so changesets stay reviewable and xref numbering stays
/// conventional). A new person is declared in its item's "people" and named
/// by xref; the legacy form describes it inline in the op.
/// </summary>
public sealed record PersonRef(string Xref, string? Name, string? Sex, IReadOnlyList<InlineFact> Facts)
{
    public bool IsInline => Name is not null;

    /// <summary>The person a field names: its item's declaration of that xref, a bare link, or a legacy inline object.</summary>
    internal static PersonRef? Read(JsonElement el, string name, IReadOnlyDictionary<string, PersonRef> people)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => people.GetValueOrDefault(v.GetString()!) ?? new PersonRef(v.GetString()!, null, null, []),
            JsonValueKind.Object => Declared(v),
            _ => throw new JsonException($"unsupported value kind for '{name}'"),
        };
    }

    /// <summary>One "people" entry, or a legacy inline person: {xref, name, sex?, facts?}.</summary>
    internal static PersonRef Declared(JsonElement v) => new(
        v.GetProperty("xref").GetString()!,
        JsonRead.Str(v, "name"),
        JsonRead.Str(v, "sex"),
        InlineFact.ReadList(v, "facts"));
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

    public static IReadOnlyList<string> StrArray(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Select(x => x.GetString()!)]
            : [];
}

using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Packages;

/// <summary>Where in a package something is: the changeset's number and the item's.</summary>
internal sealed record PackageLocation(int Changeset, int Item)
{
    public override string ToString() => $"changeset {Changeset} item {Item}";
}

/// <summary>
/// A source a citation names. Two references are one source when both give a web address and the addresses agree
/// (query, fragment and a trailing slash aside), or, when either gives none, when their titles agree.
/// </summary>
internal sealed record PackageSource(string Ref, string? Title, string? Url)
{
    public bool SameAs(PackageSource other) =>
        Url is not null && other.Url is not null ? NormalUrl(Url) == NormalUrl(other.Url)
        : Title is not null && other.Title is not null ? SourceTitles.Identity(Title) == SourceTitles.Identity(other.Title)
        : Ref == other.Ref;

    public override string ToString() => Title ?? Ref;

    static string NormalUrl(string url)
    {
        int end = url.IndexOfAny(['?', '#']);
        return (end >= 0 ? url[..end] : url).TrimEnd('/').ToLowerInvariant();
    }
}

/// <summary>One citation in a package. <see cref="Record"/> and <see cref="Fact"/> are set when it cites a fact on a record.</summary>
internal sealed record PackageCitation(
    PackageLocation Where, PackageSource Source, string? Page, string? DataText, int? Quay, string? Record = null, string? Fact = null);

/// <summary>A person a changeset creates, where the package first declares it.</summary>
internal sealed record PackagePerson(PackageLocation Where, string Xref, string Name);

/// <summary>A note an op writes on a record.</summary>
internal sealed record PackageNote(PackageLocation Where, string Record, string Text);

/// <summary>
/// What a package's changesets hold, read once for every rule: each citation, each new person and each note written,
/// with where it is. A placeholder names a record only within its changeset, so each is keyed by the changeset too.
/// </summary>
internal sealed class PackageContents
{
    static readonly string[] FactOps = ["createOrUpdateVital", "createOrUpdateCitation"];

    public List<PackageCitation> Citations { get; } = [];
    public List<PackagePerson> People { get; } = [];
    public List<PackageNote> Notes { get; } = [];

    /// <summary>The record a changeset names, keyed so that a placeholder in one changeset is not the same record as in another.</summary>
    public static string RecordKey(int changeset, string xref) => Placeholder.IsPlaceholder(xref) ? $"{changeset}:{xref}" : xref;

    /// <summary>The xref a record key was made from.</summary>
    public static string Xref(string recordKey) => recordKey[(recordKey.IndexOf(':') + 1)..];

    public static PackageContents Read(IEnumerable<PackageChangeset> changesets, ISourceRecords sources)
    {
        var contents = new PackageContents();
        foreach (var changeset in changesets)
        {
            using var json = JsonDocument.Parse(changeset.Json);
            contents.ReadChangeset(changeset.Number, json.RootElement, sources);
        }
        return contents;
    }

    void ReadChangeset(int number, JsonElement root, ISourceRecords sources)
    {
        var newSources = NewSources(root);
        PackageSource SourceOf(string reference) =>
            newSources.GetValueOrDefault(reference)
            ?? (sources.Source(reference) is { } held ? new PackageSource(reference, held.Title, held.Url) : new PackageSource(reference, null, null));

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Array(root, "items"))
        {
            var where = new PackageLocation(number, item.TryGetProperty("item", out var n) && n.TryGetInt32(out int i) ? i : 0);
            foreach (var person in Array(item, "people"))
                if (Text(person, "xref") is { } xref && Text(person, "name") is { } name && declared.Add(xref))
                    People.Add(new PackagePerson(where, xref, name));

            foreach (var op in Array(item, "ops"))
            {
                string? kind = Text(op, "op");
                string? record = Text(op, "record") is { } r ? RecordKey(number, r) : null;
                if (kind == "createOrUpdateNote" && record is not null && Text(op, "text") is { } text)
                    Notes.Add(new PackageNote(where, record, text));

                bool citesAFact = FactOps.Contains(kind) && Text(op, "fact") is not null && Text(op, "note") is null;
                foreach (var (citation, onOp) in CitationsIn(op, true))
                    Citations.Add(new PackageCitation(
                        where, SourceOf(Text(citation, "source") ?? ""), Text(citation, "page"), Text(citation, "dataText"),
                        citation.TryGetProperty("quay", out var q) && q.TryGetInt32(out int quay) ? quay : null,
                        onOp && citesAFact ? record : null, onOp && citesAFact ? Text(op, "fact")!.ToUpperInvariant() : null));
            }
        }
    }

    static Dictionary<string, PackageSource> NewSources(JsonElement root)
    {
        var result = new Dictionary<string, PackageSource>(StringComparer.Ordinal);
        foreach (var group in Array(root, "newSources"))
            if (Text(group, "xref") is { } xref)
            {
                var op = Array(group, "ops").FirstOrDefault(o => Text(o, "op") == "createOrUpdateSource");
                result[xref] = new PackageSource(xref, op.ValueKind == JsonValueKind.Object ? Text(op, "title") : null,
                    op.ValueKind == JsonValueKind.Object ? Text(op, "url") : null);
            }
        return result;
    }

    // Every citation the op holds, wherever it nests; onOp marks those in the op's own citations.
    static IEnumerable<(JsonElement Citation, bool OnOp)> CitationsIn(JsonElement node, bool top)
    {
        if (node.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in node.EnumerateObject())
        {
            if (property.Name is "citations" or "familyCitations" && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var citation in property.Value.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object))
                    yield return (citation, top && property.Name == "citations");
                continue;
            }
            var children = property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().ToList() : [property.Value];
            foreach (var child in children)
                foreach (var nested in CitationsIn(child, false)) yield return nested;
        }
    }

    static IEnumerable<JsonElement> Array(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
            : [];

    static string? Text(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

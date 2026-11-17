using System.Text.Json;
using GedCore.Contract;

namespace GedCore.Apply;

/// <summary>
/// A proposal changeset: the machine-applicable side of a research proposal.
/// The envelope carries numbered items (matching the proposal document 1:1)
/// plus <c>newSources[]</c> groups, each keyed by the xref its ops define;
/// each op is a verb × noun pair of the dialect (see <see cref="ChangeOp"/>;
/// the contract is <see cref="ChangesetDialect"/>). A source group is
/// applied unconditionally only when no item in the whole changeset cites its
/// xref (a source prepared ahead of citing it); otherwise <see
/// cref="ChangesetApplier"/> applies it only when a selected <c>--items</c>
/// entry cites it, so excluding an item never leaves its source as an
/// uncited orphan. Loaded from the JSON written at proposal time; applied by
/// <see cref="ChangesetApplier"/>.
/// </summary>
public sealed class Changeset
{
    public string? Proposal { get; init; }
    public IReadOnlyList<SourceGroup> NewSources { get; init; } = [];
    public IReadOnlyList<ChangeItem> Items { get; init; } = [];

    /// <summary>Every source op across all groups, in file order — used wherever the
    /// per-group xref doesn't matter (e.g. the fixture catalogue).</summary>
    public IEnumerable<ChangeOp> SourceOps => NewSources.SelectMany(g => g.Ops);

    public static Changeset LoadFile(string path) =>
        Parse(File.ReadAllText(path));

    /// <summary>The changeset, refused with every contract error joined when it does not satisfy the contract.</summary>
    /// <exception cref="JsonException">The changeset cannot be read.</exception>
    public static Changeset Parse(string json)
    {
        var reading = Read(json);
        return reading.Changeset ?? throw new JsonException(string.Join("; ", reading.Errors));
    }

    /// <summary>
    /// Checks <paramref name="json"/> against the changeset contract (see <see cref="ChangesetContract"/>) and reads it
    /// when it has no errors.
    /// </summary>
    public static ChangesetReading Read(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException ex) { return new ChangesetReading(null, [$"not valid JSON: {ex.Message}"]); }

        using (document)
        {
            var report = ChangesetContract.Check(document.RootElement);
            if (!report.IsValid) return new ChangesetReading(null, report.Errors);
            try { return new ChangesetReading(Build(document.RootElement), []); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return new ChangesetReading(null, [ex.Message]);
            }
        }
    }

    static Changeset Build(JsonElement root)
    {
        var newSources = new List<SourceGroup>();
        if (root.TryGetProperty("newSources", out var newSourcesEl))
            foreach (var group in newSourcesEl.EnumerateArray())
            {
                string xref = group.GetProperty("xref").GetString()!;
                newSources.Add(new SourceGroup(
                    xref,
                    [.. group.GetProperty("ops").EnumerateArray().Select(op => ChangeOp.ReadOp(op, NoPeople))])
                {
                    TextErrors = GarbledStrings(group, $"newSources {xref}:"),
                });
            }

        var items = new List<ChangeItem>();
        if (root.TryGetProperty("items", out var itemsEl))
            foreach (var item in itemsEl.EnumerateArray())
            {
                var people = People(item);
                int number = item.GetProperty("item").GetInt32();
                items.Add(new ChangeItem(
                    number,
                    item.TryGetProperty("target", out var t) ? t.GetString() : null,
                    [.. item.GetProperty("ops").EnumerateArray().Select(op => ChangeOp.ReadOp(op, people))])
                {
                    TextErrors = GarbledStrings(item, $"item {number}:"),
                });
            }

        return new Changeset
        {
            Proposal = root.TryGetProperty("proposal", out var p) ? p.GetString() : null,
            NewSources = newSources,
            Items = items,
        };
    }

    static readonly IReadOnlyDictionary<string, PersonRef> NoPeople = new Dictionary<string, PersonRef>();

    // Every string in the entry, wherever an op keeps it, so a new field is covered without naming it here.
    // The path is joined into text only for a garbled string.
    static List<string> GarbledStrings(JsonElement entry, string owner)
    {
        var errors = new List<string>();
        var path = new List<string>();
        Walk(entry);
        return errors;

        void Walk(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in el.EnumerateObject())
                    {
                        path.Add(path.Count == 0 ? property.Name : "." + property.Name);
                        Walk(property.Value);
                        path.RemoveAt(path.Count - 1);
                    }
                    break;
                case JsonValueKind.Array:
                    int index = 0;
                    foreach (var child in el.EnumerateArray())
                    {
                        path.Add($"[{index++}]");
                        Walk(child);
                        path.RemoveAt(path.Count - 1);
                    }
                    break;
                case JsonValueKind.String when GarbledText.Find(el.GetString()) is { } garbled:
                    errors.Add(GarbledText.Describe($"{owner} {string.Concat(path)}", garbled));
                    break;
            }
        }
    }

    static IReadOnlyDictionary<string, PersonRef> People(JsonElement item) =>
        item.TryGetProperty("people", out var people)
            ? people.EnumerateArray().Select(PersonRef.Declared).ToDictionary(p => p.Xref, StringComparer.Ordinal)
            : NoPeople;
}

/// <summary>A changeset read against the contract: the changeset when it has no errors.</summary>
public sealed record ChangesetReading(Changeset? Changeset, IReadOnlyList<string> Errors);

/// <summary>One <c>newSources[]</c> entry: the xref its ops define, keyed separately
/// from the ops themselves so <see cref="ChangesetApplier"/> can tell which citations
/// (across every item, not just the selected ones) would leave it an orphan.</summary>
public sealed record SourceGroup(string Xref, IReadOnlyList<ChangeOp> Ops)
{
    /// <summary>A refusal for each garbled string the group would write.</summary>
    public IReadOnlyList<string> TextErrors { get; init; } = [];
}

public sealed record ChangeItem(int Number, string? Target, IReadOnlyList<ChangeOp> Ops)
{
    /// <summary>A refusal for each garbled string the item would write.</summary>
    public IReadOnlyList<string> TextErrors { get; init; } = [];
}

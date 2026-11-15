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
    /// when it has no errors. Warnings name the legacy shapes it was read from.
    /// </summary>
    public static ChangesetReading Read(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException ex) { return new ChangesetReading(null, [$"not valid JSON: {ex.Message}"], []); }

        using (document)
        {
            var report = ChangesetContract.Check(document.RootElement);
            if (!report.IsValid) return new ChangesetReading(null, report.Errors, report.Warnings);
            try { return new ChangesetReading(Build(document.RootElement), [], report.Warnings); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return new ChangesetReading(null, [ex.Message], report.Warnings);
            }
        }
    }

    static Changeset Build(JsonElement root)
    {
        var newSources = new List<SourceGroup>();
        if (root.TryGetProperty("newSources", out var newSourcesEl))
            foreach (var group in newSourcesEl.EnumerateArray())
                newSources.Add(new SourceGroup(
                    group.GetProperty("xref").GetString()!,
                    [.. group.GetProperty("ops").EnumerateArray().Select(op => ChangeOp.ReadOp(op, NoPeople))]));

        var items = new List<ChangeItem>();
        if (root.TryGetProperty("items", out var itemsEl))
            foreach (var item in itemsEl.EnumerateArray())
            {
                var people = People(item);
                items.Add(new ChangeItem(
                    item.GetProperty("item").GetInt32(),
                    item.TryGetProperty("target", out var t) ? t.GetString() : null,
                    [.. item.GetProperty("ops").EnumerateArray().Select(op => ChangeOp.ReadOp(op, people))]));
            }

        return new Changeset
        {
            Proposal = root.TryGetProperty("proposal", out var p) ? p.GetString() : null,
            NewSources = newSources,
            Items = items,
        };
    }

    static readonly IReadOnlyDictionary<string, PersonRef> NoPeople = new Dictionary<string, PersonRef>();

    static IReadOnlyDictionary<string, PersonRef> People(JsonElement item) =>
        item.TryGetProperty("people", out var people)
            ? people.EnumerateArray().Select(PersonRef.Declared).ToDictionary(p => p.Xref, StringComparer.Ordinal)
            : NoPeople;
}

/// <summary>A changeset read against the contract: the changeset when it has no errors, and the warnings either way.</summary>
public sealed record ChangesetReading(Changeset? Changeset, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>One <c>newSources[]</c> entry: the xref its ops define, keyed separately
/// from the ops themselves so <see cref="ChangesetApplier"/> can tell which citations
/// (across every item, not just the selected ones) would leave it an orphan.</summary>
public sealed record SourceGroup(string Xref, IReadOnlyList<ChangeOp> Ops);

public sealed record ChangeItem(int Number, string? Target, IReadOnlyList<ChangeOp> Ops);

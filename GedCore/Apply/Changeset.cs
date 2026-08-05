using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// A proposal changeset: the machine-applicable side of a research proposal.
/// The envelope carries numbered items (matching the proposal document 1:1)
/// plus <c>newSources[]</c> groups, each keyed by the xref its ops define;
/// each op is a verb × noun pair of the v2 dialect (see <see cref="ChangeOp"/>;
/// the dialect is documented in the gedcom-editing skill). A source group is
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

    public static Changeset Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var newSources = new List<SourceGroup>();
        if (root.TryGetProperty("newSources", out var newSourcesEl))
            foreach (var group in newSourcesEl.EnumerateArray())
                newSources.Add(new SourceGroup(
                    group.GetProperty("xref").GetString()!,
                    [.. group.GetProperty("ops").EnumerateArray().Select(ChangeOp.ReadOp)]));

        var items = new List<ChangeItem>();
        if (root.TryGetProperty("items", out var itemsEl))
            foreach (var item in itemsEl.EnumerateArray())
                items.Add(new ChangeItem(
                    item.GetProperty("item").GetInt32(),
                    item.TryGetProperty("target", out var t) ? t.GetString() : null,
                    [.. item.GetProperty("ops").EnumerateArray().Select(ChangeOp.ReadOp)]));

        return new Changeset
        {
            Proposal = root.TryGetProperty("proposal", out var p) ? p.GetString() : null,
            NewSources = newSources,
            Items = items,
        };
    }
}

/// <summary>One <c>newSources[]</c> entry: the xref its ops define, keyed separately
/// from the ops themselves so <see cref="ChangesetApplier"/> can tell which citations
/// (across every item, not just the selected ones) would leave it an orphan.</summary>
public sealed record SourceGroup(string Xref, IReadOnlyList<ChangeOp> Ops);

public sealed record ChangeItem(int Number, string? Target, IReadOnlyList<ChangeOp> Ops);

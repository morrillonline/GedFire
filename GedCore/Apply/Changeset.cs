using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// A proposal changeset: the machine-applicable side of a research proposal.
/// The envelope carries numbered items (matching the proposal document 1:1)
/// plus always-applied source ops; each op is a verb × noun pair of the v2
/// dialect (see <see cref="ChangeOp"/>; the dialect is documented in the
/// gedcom-editing skill). Loaded from the JSON written at proposal time;
/// applied by <see cref="ChangesetApplier"/>.
/// </summary>
public sealed class Changeset
{
    public string? Proposal { get; init; }
    public IReadOnlyList<ChangeOp> SourceOps { get; init; } = [];
    public IReadOnlyList<ChangeItem> Items { get; init; } = [];

    public static Changeset LoadFile(string path) =>
        Parse(File.ReadAllText(path));

    public static Changeset Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var sourceOps = new List<ChangeOp>();
        if (root.TryGetProperty("newSources", out var newSources))
            foreach (var group in newSources.EnumerateArray())
                foreach (var op in group.GetProperty("ops").EnumerateArray())
                    sourceOps.Add(ChangeOp.ReadOp(op));

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
            SourceOps = sourceOps,
            Items = items,
        };
    }
}

public sealed record ChangeItem(int Number, string? Target, IReadOnlyList<ChangeOp> Ops);

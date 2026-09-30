namespace GedCore.Apply;

/// <summary>
/// Keeps an edit to a source from silently changing every fact that cites it.
/// When an item updates an existing source that more than one structure cites,
/// the update is applied to a copy (or to an existing source that is exactly
/// the updated one) and the item's own citations of the original are pointed
/// at that instead. The original and every other citation of it stay as they
/// were. An unshared source is updated in place, as before.
/// </summary>
internal static class SharedSourceCopier
{
    /// <summary>The number of distinct structures (facts, notes, FAMs) citing the source.</summary>
    public static int CitingStructures(GedDocument doc, string xref)
    {
        var citing = new HashSet<GedRecord>(ReferenceEqualityComparer.Instance);
        foreach (var root in doc.Records) Collect(root, xref, citing);
        return citing.Count;
    }

    static void Collect(GedRecord node, string xref, HashSet<GedRecord> citing)
    {
        foreach (var child in node.Children)
        {
            if (child.Tag == "SOUR" && child.Value == xref) citing.Add(node);
            Collect(child, xref, citing);
        }
    }

    /// <summary>
    /// Reject an item that would update a shared source without citing it: the
    /// change has no fact to attach to, and applying it in place would change
    /// every citing fact.
    /// </summary>
    public static void ValidateItem(GedDocument doc, ChangeItem item, List<string> errors)
    {
        foreach (var op in item.Ops.OfType<CreateOrUpdateSourceOp>())
        {
            if (!TryExisting(doc, op.Xref, out var source) || !op.UpdatesAnything) continue;
            int citers = CitingStructures(doc, op.Xref);
            if (citers < 2 || !WouldChange(op, source)) continue;
            if (item.Ops.Any(o => o.CitedSources.Contains(op.Xref))) continue;
            errors.Add($"{op.Kind} {op.Xref}: cited by {citers} structures, so updating it would change what every " +
                       $"one of them says; cite {op.Xref} in this item on the fact that needs the change and the " +
                       "update is applied to a copy used only there");
        }
    }

    /// <summary>
    /// Before an item's ops run: for each shared source the item updates, create
    /// the copy (or find the exact match) and register the redirect so the
    /// item's citations of the original resolve to it.
    /// </summary>
    public static void PrepareItem(ApplyState state, ChangeItem item, List<string> log)
    {
        foreach (var op in item.Ops.OfType<CreateOrUpdateSourceOp>())
        {
            if (!TryExisting(state.Doc, op.Xref, out var source) || !op.UpdatesAnything) continue;
            if (state.ItemSourceRedirects.ContainsKey(op.Xref)) continue;
            int citers = CitingStructures(state.Doc, op.Xref);
            if (citers < 2) continue;

            var copy = CopyOf(state, source);
            var changes = new List<string>();
            op.ApplyFields(copy, changes);
            if (changes.Count == 0) continue;

            var existing = SourceMatcher.FindExactMatchOf(state.Doc, copy, except: source);
            if (existing is not null)
            {
                state.RedirectSource(op.Xref, existing.Xref!);
                log.Add($"{op.Kind} {op.Xref}: shared by {citers} structures; the updated source already exists " +
                        $"as {existing.Xref}, used for item {item.Number} instead of a copy");
                continue;
            }

            state.AddRecord("SOUR", copy);
            state.RedirectSource(op.Xref, copy.Xref!);
            log.Add($"{op.Kind} {op.Xref}: shared by {citers} structures; copied to {copy.Xref} for item " +
                    $"{item.Number} ({string.Join("; ", changes)})");
        }
    }

    static bool TryExisting(GedDocument doc, string xref, out GedRecord source)
    {
        source = null!;
        if (!doc.ByXref.TryGetValue(xref, out var found) || found.Tag != "SOUR") return false;
        source = found;
        return true;
    }

    static bool WouldChange(CreateOrUpdateSourceOp op, GedRecord source)
    {
        var probe = new GedRecord(0, source.Xref, "SOUR", "");
        foreach (var child in source.Children) NodeBuilder.Attach(probe, NodeBuilder.Relevel(child, 1));
        var changes = new List<string>();
        op.ApplyFields(probe, changes);
        return changes.Count > 0;
    }

    static GedRecord CopyOf(ApplyState state, GedRecord source)
    {
        string xref = XrefMinter.MintNext(state.Doc.Records.Where(r => r.Tag == "SOUR").Select(r => r.Xref ?? ""), "S");
        var copy = new GedRecord(0, xref, "SOUR", "");
        foreach (var child in source.Children) NodeBuilder.Attach(copy, NodeBuilder.Relevel(child, 1));
        return copy;
    }
}

using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// One tag's disposition when both the survivor and the duplicate carry it
/// with a different value: keep the survivor's, keep the duplicate's, or
/// replace both with an explicit merged value. Citations attach to whichever
/// instance survives — the mechanism for citing a fact that was previously
/// uncited on both sides, now supported by the evidence that identified the
/// duplicate.
/// </summary>
public sealed record MergeFactResolution(string Fact, string Keep, EventValue? Value,
                                    IReadOnlyList<Citation> Citations)
{
    internal static MergeFactResolution Read(JsonElement el) => new(
        JsonRead.Req(el, "fact", "mergePerson.facts"),
        JsonRead.Str(el, "keep") ?? "value",
        EventValue.Read(el, "value"),
        Citation.ReadAll(el));
}

/// <summary>
/// Merge: fold a duplicate INDI into a survivor. Facts present on only one
/// side are unioned onto the survivor automatically, citations intact. A
/// fact tag present on BOTH sides with a different value/date/place is a
/// conflict the composer must resolve explicitly via "facts" — the tool
/// never guesses which record's claim is better evidence; that judgment
/// belongs to whoever reviewed the records closely enough to identify the
/// duplicate in the first place.
///
/// Every FAMS/FAMC the duplicate held is relocated onto the survivor (a role
/// the survivor already holds in the same family is dropped, not
/// duplicated); every remaining pointer to the duplicate elsewhere in the
/// file (FAM HUSB/WIFE/CHIL back-references) is redirected to the survivor.
/// The duplicate record is then deleted and a NOTE documents the merge on
/// the survivor for the audit trail.
/// </summary>
public sealed class MergePersonOp : ChangeOp
{
    public override string Kind => "mergePerson";

    public required string Survivor { get; init; }
    public required string Duplicate { get; init; }
    public IReadOnlyList<MergeFactResolution> Facts { get; init; } = [];
    public string? Note { get; init; }

    internal static MergePersonOp Read(JsonElement el) => new()
    {
        Survivor = JsonRead.Req(el, "survivor", "mergePerson"),
        Duplicate = JsonRead.Req(el, "duplicate", "mergePerson"),
        Facts = el.TryGetProperty("facts", out var f)
            ? [.. f.EnumerateArray().Select(MergeFactResolution.Read)]
            : [],
        Note = JsonRead.Str(el, "note"),
    };

    private string Context => $"{Kind} {Duplicate} into {Survivor}";
    private static readonly HashSet<string> RelationshipTags = ["FAMC", "FAMS", "UID"];

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (Survivor == Duplicate)
        { errors.Add($"{Context}: survivor and duplicate are the same xref"); return; }
        if (OpChecks.RejectVoid(Context, Survivor, errors)) return;
        if (OpChecks.RejectVoid(Context, Duplicate, errors)) return;
        if (!ctx.Known(Survivor)) { errors.Add($"{Context}: survivor not in file"); return; }

        var duplicate = ctx.Existing(Duplicate);
        if (duplicate is null) return;   // already merged away, or never existed — apply-time no-op
        if (duplicate.Tag != "INDI") { errors.Add($"{Context}: duplicate is not an INDI record"); return; }

        var survivor = ctx.Existing(Survivor);
        if (survivor is null) return;   // created earlier this run — nothing to inspect yet
        if (survivor.Tag != "INDI") { errors.Add($"{Context}: survivor is not an INDI record"); return; }

        var resolved = new HashSet<string>(Facts.Select(f => f.Fact));
        foreach (var tag in duplicate.Children.Select(c => c.Tag).Distinct())
        {
            if (RelationshipTags.Contains(tag)) continue;
            var onSurvivor = survivor.ChildrenByTag(tag).ToList();
            if (onSurvivor.Count == 0) continue;   // pure union, no conflict
            bool differs = duplicate.ChildrenByTag(tag)
                .Any(d => !onSurvivor.Any(s => FactsEqual(s, d)));
            if (differs && !resolved.Contains(tag))
                errors.Add($"{Context}: {tag} differs on both records; resolve in \"facts\"");
        }

        foreach (var res in Facts)
            foreach (var cit in res.Citations)
            {
                if (OpChecks.RejectVoid($"{Context}: {res.Fact} resolution", cit.Source, errors)) continue;
                if (!ctx.Known(cit.Source))
                    errors.Add($"{Context}: {res.Fact} resolution citation source {cit.Source} unknown");
            }
    }

    private static bool FactsEqual(GedRecord a, GedRecord b) =>
        a.Value == b.Value
        && a.FirstChild("DATE")?.Value == b.FirstChild("DATE")?.Value
        && a.FirstChild("PLAC")?.Value == b.FirstChild("PLAC")?.Value;

    internal override void Apply(ApplyState state, List<string> log)
    {
        if (!state.Doc.ByXref.TryGetValue(Duplicate, out var duplicate))
        {
            log.Add($"{Context}: no-op (duplicate already gone)");
            return;
        }
        var survivor = state.Doc.ByXref[Survivor];
        var resolved = Facts.ToDictionary(f => f.Fact);
        var changes = new List<string>();

        foreach (var tag in duplicate.Children.Select(c => c.Tag).Distinct().ToList())
        {
            if (RelationshipTags.Contains(tag)) continue;
            if (resolved.TryGetValue(tag, out var resolution))
                ApplyResolution(state, survivor, duplicate, tag, resolution, changes);
            else if (!survivor.ChildrenByTag(tag).Any())
                foreach (var node in duplicate.ChildrenByTag(tag).ToList())
                {
                    duplicate.Children.Remove(node);
                    NodeBuilder.InsertFact(survivor, NodeBuilder.Relevel(node, survivor.Level + 1));
                    changes.Add($"{tag} carried over from {Duplicate}");
                    state.Mutated();
                    state.Touch(survivor);
                }
        }

        foreach (var relTag in new[] { "FAMS", "FAMC" })
            foreach (var node in duplicate.ChildrenByTag(relTag).ToList())
            {
                duplicate.Children.Remove(node);
                if (survivor.ChildrenByTag(relTag).Any(s => s.Value == node.Value))
                {
                    changes.Add($"{relTag} {node.Value} already on survivor; duplicate's link dropped");
                    continue;
                }
                int anchor = relTag == "FAMS"
                    ? survivor.Children.FindIndex(c => c.Tag is "FAMC" or "NOTE" or "SNOTE" or "UID")
                    : survivor.Children.FindIndex(c => c.Tag is "NOTE" or "SNOTE" or "UID");
                NodeBuilder.Attach(survivor, NodeBuilder.Relevel(node, 1), at: anchor < 0 ? null : anchor);
                changes.Add($"{relTag} {node.Value} carried over from {Duplicate}");
                state.Mutated();
                state.Touch(survivor);
            }

        int redirected = RedirectPointers(state, Duplicate, Survivor);
        if (redirected > 0)
        {
            DedupeChilLinks(state.Doc, Survivor);
            changes.Add($"{redirected} pointer(s) redirected {Duplicate} -> {Survivor}");
        }

        state.RemoveRecord(duplicate);

        string note = Note ?? $"Merged duplicate {Duplicate} into this record.";
        int noteAnchor = survivor.Children.FindIndex(c => c.Tag == "UID");
        NodeBuilder.Attach(survivor, NodeBuilder.NewNode(1, "NOTE", note),
                           at: noteAnchor < 0 ? null : noteAnchor);
        state.Mutated();
        state.Touch(survivor);

        log.Add($"{Context}: merged ({string.Join("; ", changes)})");
    }

    /// <summary>File convention: NAME leads every INDI record; other facts follow the usual layout.</summary>
    private static void PlaceFact(GedRecord survivor, string tag, GedRecord node)
    {
        if (tag == "NAME") NodeBuilder.Attach(survivor, node, at: 0);
        else NodeBuilder.InsertFact(survivor, node);
    }

    private static void ApplyResolution(ApplyState state, GedRecord survivor, GedRecord duplicate,
                                        string tag, MergeFactResolution resolution, List<string> changes)
    {
        var duplicateNodes = duplicate.ChildrenByTag(tag).ToList();
        foreach (var node in duplicateNodes) duplicate.Children.Remove(node);

        switch (resolution.Keep)
        {
            case "survivor":
                changes.Add($"{tag}: kept survivor's value");
                break;

            case "duplicate":
                foreach (var node in survivor.ChildrenByTag(tag).ToList())
                    survivor.Children.Remove(node);
                foreach (var node in duplicateNodes)
                    PlaceFact(survivor, tag, NodeBuilder.Relevel(node, survivor.Level + 1));
                changes.Add($"{tag}: kept {duplicate.Xref}'s value");
                break;

            default:   // "value" — an explicit merged value, replacing both sides
                foreach (var node in survivor.ChildrenByTag(tag).ToList())
                    survivor.Children.Remove(node);
                var fresh = NodeBuilder.EventNode(survivor.Level + 1, tag, resolution.Value, []);
                PlaceFact(survivor, tag, fresh);
                changes.Add($"{tag}: set explicit merged value");
                break;
        }

        foreach (var node in survivor.ChildrenByTag(tag))
            foreach (var cit in resolution.Citations)
                NodeBuilder.Attach(node, NodeBuilder.CitationNode(node.Level + 1, cit));

        state.Mutated();
        state.Touch(survivor);
    }

    private static int RedirectPointers(ApplyState state, string from, string to)
    {
        int count = 0;
        foreach (var rec in state.Doc.Records)
            count += RedirectPointers(state, rec, from, to);
        return count;
    }

    private static int RedirectPointers(ApplyState state, GedRecord node, string from, string to)
    {
        int count = 0;
        foreach (var child in node.Children)
        {
            if (child.Value == from)
            {
                child.SetValue(to);
                state.Touch(child);
                count++;
            }
            count += RedirectPointers(state, child, from, to);
        }
        return count;
    }

    /// <summary>Defensive: collapse a FAM record left with two CHIL lines pointing at the same survivor.</summary>
    private static void DedupeChilLinks(GedDocument doc, string xref)
    {
        foreach (var rec in doc.Records.Where(r => r.Tag == "FAM"))
        {
            var dups = rec.Children.Where(c => c.Tag == "CHIL" && c.Value == xref).Skip(1).ToList();
            foreach (var d in dups) rec.Children.Remove(d);
        }
    }
}

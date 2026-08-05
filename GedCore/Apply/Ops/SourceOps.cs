using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// CreateOrUpdate for the Source noun. Key: xref. Absent → create a SOUR
/// record (AUTH, TITL, and a composed provenance NOTE). Present → compare the
/// provided fields and update only those that differ; fields the request
/// omits are left alone.
/// </summary>
public sealed class CreateOrUpdateSourceOp : ChangeOp
{
    public override string Kind => "createOrUpdateSource";

    public required string Xref { get; init; }
    public string? Auth { get; init; }
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Accessed { get; init; }

    internal static CreateOrUpdateSourceOp Read(JsonElement el) => new()
    {
        Xref = JsonRead.Req(el, "xref", "createOrUpdateSource"),
        Auth = JsonRead.Str(el, "auth"),
        Title = JsonRead.Str(el, "title"),
        Url = JsonRead.Str(el, "url"),
        Accessed = JsonRead.Str(el, "accessed"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Kind, Xref, errors)) return;
        var existing = ctx.Existing(Xref);
        if (existing is null)
        {
            if (Title is null && !ctx.Planned.Contains(Xref))
                errors.Add($"{Kind} {Xref}: title required to create a source");
            ctx.Planned.Add(Xref);
        }
        else if (existing.Tag != "SOUR")
            errors.Add($"{Kind} {Xref}: not a SOUR record");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var existing = state.Doc.ByXref.GetValueOrDefault(Xref);
        if (existing is null)
        {
            var rec = new GedRecord(0, Xref, "SOUR", "");
            if (Auth is not null) NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "AUTH", Auth));
            NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "TITL", Title!));
            NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "NOTE", ComposedNote(Title!)));
            state.AddRecord("SOUR", rec);
            log.Add($"{Kind} {Xref}: created");
            return;
        }

        var changes = new List<string>();
        if (Auth is not null) UpsertField(existing, "AUTH", Auth, at: 0, changes);
        if (Title is not null)
            UpsertField(existing, "TITL", Title, at: existing.FirstChild("AUTH") is null ? 0 : 1, changes);
        if (Url is not null || Accessed is not null)
        {
            string? title = Title ?? existing.FirstChild("TITL")?.Value;
            if (title is not null)
                UpsertField(existing, "NOTE", ComposedNote(title), at: null, changes);
        }

        if (changes.Count > 0) { state.Mutated(); state.Touch(existing); }
        log.Add(changes.Count > 0
            ? $"{Kind} {Xref}: updated ({string.Join("; ", changes)})"
            : $"{Kind} {Xref}: no-op (already matches)");
    }

    private string ComposedNote(string title)
    {
        string note = title;
        if (Url is not null) note += $", online at {Url}";
        if (Accessed is not null) note += $" (accessed {Accessed})";
        return note + ".";
    }

    private static void UpsertField(GedRecord source, string tag, string value,
                                    int? at, List<string> changes)
    {
        var field = source.FirstChild(tag);
        if (field is null)
        {
            NodeBuilder.Attach(source, NodeBuilder.NewNode(1, tag, value), at);
            changes.Add($"{tag} added '{value}'");
        }
        else if (field.Value != value)
        {
            changes.Add($"{tag} '{field.Value}' → '{value}'");
            field.SetValue(value);
        }
    }
}

/// <summary>
/// Delete for the Source noun. Refuses to delete a source any structure still
/// cites — deletion never silently orphans provenance. Absent → no-op.
/// </summary>
public sealed class DeleteSourceOp : ChangeOp
{
    public override string Kind => "deleteSource";

    public required string Xref { get; init; }

    internal static DeleteSourceOp Read(JsonElement el) => new()
    {
        Xref = JsonRead.Req(el, "xref", "deleteSource"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        var existing = ctx.Existing(Xref);
        if (existing is null) return;   // no-op at apply time
        if (existing.Tag != "SOUR") { errors.Add($"{Kind} {Xref}: not a SOUR record"); return; }

        int references = SourceCitations.Count(ctx.RecordsOfType("INDI")
            .Concat(ctx.RecordsOfType("FAM"))
            .Concat(ctx.RecordsOfType("SOUR")), Xref);
        if (references > 0)
            errors.Add($"{Kind} {Xref}: still cited {references} time(s); remove the citations first");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var existing = state.Doc.ByXref.GetValueOrDefault(Xref);
        if (existing is null)
        {
            log.Add($"{Kind} {Xref}: no-op (absent)");
            return;
        }
        state.RemoveRecord(existing);
        log.Add($"{Kind} {Xref}: deleted");
    }
}

/// <summary>Counts <c>SOUR</c> citation pointers naming a given source xref, shared by
/// <see cref="DeleteSourceOp"/>'s still-cited guard and <see cref="ChangesetApplier"/>'s
/// post-apply orphan-source check.</summary>
internal static class SourceCitations
{
    public static int Count(IEnumerable<GedRecord> roots, string xref) =>
        roots.Sum(r => CountIn(r, xref));

    private static int CountIn(GedRecord node, string xref) =>
        node.Children.Sum(c => (c.Tag == "SOUR" && c.Value == xref ? 1 : 0) + CountIn(c, xref));
}

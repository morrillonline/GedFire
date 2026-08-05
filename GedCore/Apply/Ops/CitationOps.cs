using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// CreateOrUpdate for the Citation noun: attach source citation(s) to a fact
/// that already exists (a citation cannot create its fact). Key: (record,
/// fact instance, source xref). Source absent on the fact → add; present with
/// identical fields → no-op; present with differing fields → update in place.
/// </summary>
public sealed class CreateOrUpdateCitationOp : ChangeOp
{
    public override string Kind => "createOrUpdateCitation";

    public required string Record { get; init; }
    public required string Fact { get; init; }
    public FactMatch? Match { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    internal override IEnumerable<string> CitedSources => Citations.Select(c => c.Source);

    internal static CreateOrUpdateCitationOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "createOrUpdateCitation"),
        Fact = JsonRead.Req(el, "fact", "createOrUpdateCitation"),
        Match = FactMatch.Read(el, "match"),
        Citations = Citation.ReadAll(el),
    };

    private string Context => $"{Kind} {Fact} on {Record}";

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Context}: target not in file"); return; }
        OpChecks.CitationsRequired(Context, Citations, errors);
        OpChecks.CitationsValid(ctx, Context, Citations, errors);

        var target = ctx.Existing(Record);
        if (target is null) return;
        var res = Resolve.Fact(target, Fact, Match);
        if (res.Ambiguous)
            errors.Add($"{Context}: ambiguous — {res.MatchCount} {Fact} facts match; refine \"match\"");
        else if (res.Fact is null)
            errors.Add($"{Context}: no such fact — a citation cannot create its fact " +
                       "(use createOrUpdateVital to assert it)");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[Record];
        // validation guarantees resolution except on records created earlier
        // in this run, which it could not inspect — fail before any write
        var fact = Resolve.Fact(target, Fact, Match).Fact
            ?? throw new InvalidOperationException($"{Context}: no such fact on the record as applied");

        var changes = new List<string>();
        foreach (var cit in Citations)
            if (NodeBuilder.UpsertCitation(state, fact, cit) is string change)
                changes.Add(change);

        log.Add(changes.Count > 0
            ? $"{Context}: {string.Join("; ", changes)}"
            : $"{Context}: no-op (already cited identically)");
    }
}

/// <summary>
/// Delete for the Citation noun: remove one source's citation from a fact.
/// Absent fact or absent citation → no-op.
/// </summary>
public sealed class DeleteCitationOp : ChangeOp
{
    public override string Kind => "deleteCitation";

    public required string Record { get; init; }
    public required string Fact { get; init; }
    public FactMatch? Match { get; init; }
    public required string Source { get; init; }

    internal static DeleteCitationOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "deleteCitation"),
        Fact = JsonRead.Req(el, "fact", "deleteCitation"),
        Match = FactMatch.Read(el, "match"),
        Source = JsonRead.Req(el, "source", "deleteCitation"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid($"{Kind} {Fact} on {Record}", Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Kind} {Fact} on {Record}: target not in file"); return; }
        var target = ctx.Existing(Record);
        if (target is not null && Resolve.Fact(target, Fact, Match).Ambiguous)
            errors.Add($"{Kind} {Fact} on {Record}: ambiguous — multiple {Fact} facts match; refine \"match\"");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[Record];
        var fact = Resolve.Fact(target, Fact, Match).Fact;
        var citation = fact is null ? null : Resolve.CitationOnStructure(fact, Source);
        if (citation is null)
        {
            log.Add($"{Kind} {Fact} on {Record}: no-op ({Source} not cited)");
            return;
        }
        fact!.Children.Remove(citation);
        state.Mutated();
        state.Touch(target);
        log.Add($"{Kind} {Fact} on {Record}: removed citation {Source}");
    }
}

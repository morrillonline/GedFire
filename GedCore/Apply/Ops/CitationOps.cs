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
        var target = state.Doc.ByXref[state.Resolve(Record)];
        var citations = state.ResolveCitations(Citations);
        // validation guarantees resolution except on records created earlier
        // in this run, which it could not inspect — fail before any write
        var fact = Resolve.Fact(target, Fact, Match).Fact
            ?? throw new InvalidOperationException($"{Context}: no such fact on the record as applied");

        var changes = CitationReconciler.Upsert(state, fact, citations);

        log.Add(changes.Count > 0
            ? $"{Context}: {string.Join("; ", changes)}"
            : $"{Context}: no-op (already cited identically)");
    }
}

/// <summary>
/// Delete for the Citation noun: remove one source's citation from a fact.
/// Absent fact or absent citation → no-op. When the fact cites the source on
/// several pages, "page" says which citation to remove.
/// </summary>
public sealed class DeleteCitationOp : ChangeOp
{
    public override string Kind => "deleteCitation";

    public required string Record { get; init; }
    public required string Fact { get; init; }
    public FactMatch? Match { get; init; }
    public required string Source { get; init; }
    public string? Page { get; init; }

    internal static DeleteCitationOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "deleteCitation"),
        Fact = JsonRead.Req(el, "fact", "deleteCitation"),
        Match = FactMatch.Read(el, "match"),
        Source = JsonRead.Req(el, "source", "deleteCitation"),
        Page = JsonRead.Str(el, "page"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid($"{Kind} {Fact} on {Record}", Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Kind} {Fact} on {Record}: target not in file"); return; }
        var target = ctx.Existing(Record);
        if (target is null) return;
        var res = Resolve.Fact(target, Fact, Match);
        if (res.Ambiguous)
            errors.Add($"{Kind} {Fact} on {Record}: ambiguous — multiple {Fact} facts match; refine \"match\"");
        else if (res.Fact is not null && Page is null &&
                 Resolve.CitationsOnStructure(res.Fact, Source).Count > 1)
            errors.Add(AmbiguousCitation(Record, Resolve.CitationsOnStructure(res.Fact, Source)));
    }

    private string AmbiguousCitation(string record, IReadOnlyList<GedRecord> cited) =>
        $"{Kind} {Fact} on {record}: {Source} is cited {cited.Count} times " +
        $"(pages: {CitationReconciler.DescribePages(cited)}); add \"page\" to say which to remove";

    internal override void Apply(ApplyState state, List<string> log)
    {
        // Source is compared against the actual written SOUR pointer value
        // (Resolve.CitationOnStructure), which is always the real xref — so
        // a placeholder naming a source created earlier in this same
        // changeset must be resolved before that comparison, the same as
        // any createOrUpdate op's citation source.
        var record = state.Resolve(Record);
        var source = state.Resolve(Source);
        var target = state.Doc.ByXref[record];
        var fact = Resolve.Fact(target, Fact, Match).Fact;
        var cited = fact is null ? [] : Resolve.CitationsOnStructure(fact, source);
        if (Page is null && cited.Count > 1)
            throw new InvalidOperationException(AmbiguousCitation(record, cited));
        var citation = Page is null
            ? cited.FirstOrDefault()
            : cited.FirstOrDefault(c => c.FirstChild("PAGE")?.Value == Page);
        if (citation is null)
        {
            log.Add(cited.Count == 0
                ? $"{Kind} {Fact} on {record}: no-op ({source} not cited)"
                : $"{Kind} {Fact} on {record}: no-op ({source} not cited on page \"{Page}\")");
            return;
        }
        fact!.Children.Remove(citation);
        state.Mutated();
        state.Touch(target);
        log.Add($"{Kind} {Fact} on {record}: removed citation {source}");
    }
}

using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// Which structure's citations a citation op addresses: one fact on the
/// record, one note on the record (named by its exact text), or — when
/// neither is named — a family record's own citations (the relationship
/// provenance the spouse, child and parent ops write on the FAM).
/// </summary>
internal sealed record CitationAddress(string Record, string? Fact, FactMatch? Match, string? Note)
{
    private const int NoteExcerptLength = 30;

    public string Context(string kind)
    {
        if (Fact is not null) return $"{kind} {Fact} on {Record}";
        if (Note is null) return $"{kind} on {Record} (record level)";
        string excerpt = Note.Length > NoteExcerptLength ? Note[..NoteExcerptLength] + "…" : Note;
        return $"{kind} on {Record} (note \"{excerpt}\")";
    }

    /// <summary>Selector problems that need no document: conflicting selectors, or a record-level address on a non-family.</summary>
    public void ValidateShape(ResolutionContext ctx, string context, List<string> errors)
    {
        if (Fact is not null && Note is not null)
            errors.Add($"{context}: name either \"fact\" or \"note\", not both");
        if (Match is not null && Fact is null)
            errors.Add($"{context}: \"match\" selects a fact; omit it, or name the fact");
        if (Fact is null && Note is null && !IsFamily(ctx))
            errors.Add($"{context}: a citation without \"fact\" or \"note\" addresses a family record's own " +
                       $"citations; {Record} is not a family — name the fact or note to cite");
    }

    private bool IsFamily(ResolutionContext ctx)
    {
        var existing = ctx.Existing(Record);
        if (existing is not null) return existing.Tag == "FAM";
        return !Placeholder.IsPlaceholder(Record)
               || (ctx.Placeholders.TryGetKind(Record, out var kind) && kind == PlaceholderKind.Family);
    }

    /// <summary>The structure carrying the citations, or null when the named fact or note is absent.</summary>
    public GedRecord? Resolve(GedRecord record)
    {
        if (Fact is not null) return Apply.Resolve.Fact(record, Fact, Match).Fact;
        if (Note is null) return record;
        string text = NodeBuilder.NormalizeText(Note);
        return record.ChildrenByTag("NOTE").FirstOrDefault(n => n.FullValue() == text);
    }

    public string MissingStructure =>
        Note is not null
            ? "no note has that text (createOrUpdateNote creates one)"
            : "no such fact — a citation cannot create its fact (use createOrUpdateVital to assert it)";
}

/// <summary>
/// CreateOrUpdate for the Citation noun: attach source citation(s) to a fact
/// or note that already exists (a citation cannot create what it cites), or —
/// with neither named — to a family record itself. Key: (record, structure,
/// source xref). Source absent on the structure → add; present with identical
/// fields → no-op; present with differing fields → update in place.
/// </summary>
public sealed class CreateOrUpdateCitationOp : ChangeOp
{
    public override string Kind => "createOrUpdateCitation";

    public required string Record { get; init; }
    public string? Fact { get; init; }
    public FactMatch? Match { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    internal override IEnumerable<string> CitedSources => Citations.Select(c => c.Source);

    internal static CreateOrUpdateCitationOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "createOrUpdateCitation"),
        Fact = JsonRead.Str(el, "fact"),
        Match = FactMatch.Read(el, "match"),
        Note = JsonRead.Str(el, "note"),
        Citations = Citation.ReadAll(el),
    };

    private CitationAddress Address => new(Record, Fact, Match, Note);
    private string Context => Address.Context(Kind);

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Context}: target not in file"); return; }
        OpChecks.CitationsRequired(Context, Citations, errors);
        OpChecks.CitationsValid(ctx, Context, Citations, errors);
        Address.ValidateShape(ctx, Context, errors);

        var target = ctx.Existing(Record);
        if (target is null || (Fact is null && Note is null)) return;
        if (Fact is not null && Resolve.Fact(target, Fact, Match) is { Ambiguous: true } ambiguous)
            errors.Add($"{Context}: ambiguous — {ambiguous.MatchCount} {Fact} facts match; refine \"match\"");
        else if (Address.Resolve(target) is null)
            errors.Add($"{Context}: {Address.MissingStructure}");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[state.Resolve(Record)];
        var citations = state.ResolveCitations(Citations);
        // validation guarantees resolution except on records created earlier
        // in this run, which it could not inspect — fail before any write
        var structure = Address.Resolve(target)
            ?? throw new InvalidOperationException($"{Context}: {Address.MissingStructure} on the record as applied");

        var changes = CitationReconciler.Upsert(state, structure, citations);

        log.Add(changes.Count > 0
            ? $"{Context}: {string.Join("; ", changes)}"
            : $"{Context}: no-op (already cited identically)");
    }
}

/// <summary>
/// Delete for the Citation noun: remove one source's citation from a fact, a
/// note, or a family record itself. Absent structure or absent citation →
/// no-op. When the structure cites the source on several pages, "page" says
/// which citation to remove.
/// </summary>
public sealed class DeleteCitationOp : ChangeOp
{
    public override string Kind => "deleteCitation";

    public required string Record { get; init; }
    public string? Fact { get; init; }
    public FactMatch? Match { get; init; }
    public string? Note { get; init; }
    public required string Source { get; init; }
    public string? Page { get; init; }

    internal static DeleteCitationOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "deleteCitation"),
        Fact = JsonRead.Str(el, "fact"),
        Match = FactMatch.Read(el, "match"),
        Note = JsonRead.Str(el, "note"),
        Source = JsonRead.Req(el, "source", "deleteCitation"),
        Page = JsonRead.Str(el, "page"),
    };

    private CitationAddress Address => new(Record, Fact, Match, Note);
    private string Context => Address.Context(Kind);

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Context}: target not in file"); return; }
        Address.ValidateShape(ctx, Context, errors);

        var target = ctx.Existing(Record);
        if (target is null) return;
        if (Fact is not null && Resolve.Fact(target, Fact, Match) is { Ambiguous: true })
        {
            errors.Add($"{Context}: ambiguous — multiple {Fact} facts match; refine \"match\"");
            return;
        }
        if (Fact is null && Note is null && target.Tag != "FAM") return;   // shape error already reported
        if (Address.Resolve(target) is { } structure && Page is null &&
            Resolve.CitationsOnStructure(structure, Source).Count > 1)
            errors.Add(AmbiguousCitation(Resolve.CitationsOnStructure(structure, Source)));
    }

    private string AmbiguousCitation(IReadOnlyList<GedRecord> cited) =>
        $"{Context}: {Source} is cited {cited.Count} times " +
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
        var structure = Address.Resolve(target);
        var cited = structure is null ? [] : Resolve.CitationsOnStructure(structure, source);
        if (Page is null && cited.Count > 1)
            throw new InvalidOperationException(AmbiguousCitation(cited));
        var citation = Page is null
            ? cited.FirstOrDefault()
            : cited.FirstOrDefault(c => c.FirstChild("PAGE")?.Value == Page);
        if (citation is null)
        {
            log.Add(cited.Count == 0
                ? $"{Context}: no-op ({source} not cited)"
                : $"{Context}: no-op ({source} not cited on page \"{Page}\")");
            return;
        }
        structure!.Children.Remove(citation);
        state.Mutated();
        state.Touch(target);
        log.Add($"{Context}: removed citation {source}");
    }
}

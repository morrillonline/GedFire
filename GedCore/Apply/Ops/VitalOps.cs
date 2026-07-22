using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// CreateOrUpdate for the Vital noun: a dated/placed event (BIRT, DEAT, MARR,
/// CENS, BURI, …) or a text-payload fact (NAME) on a person or family record.
/// Key: (record, fact tag [, match selector]). Absent → create; every
/// requested field equal → no-op; different → replace unconditionally,
/// logging the old value. Fields the request omits are left untouched.
///
/// A repeatable fact (CENS, RESI, OCCU… — anything not in
/// <see cref="SingleValuedFacts"/>) is protected: with no <c>match</c>, an op
/// that would overwrite an existing instance's value is refused at validation
/// so a second occurrence can never silently clobber the first. Add a new
/// instance with <c>"mode":"add"</c>; update a specific one with <c>match</c>.
/// </summary>
public sealed class CreateOrUpdateVitalOp : ChangeOp
{
    public override string Kind => "createOrUpdateVital";

    public required string Record { get; init; }
    public required string Fact { get; init; }
    public required EventValue Value { get; init; }
    public FactMatch? Match { get; init; }

    /// <summary>"upsert" (default) updates a matched/sole instance or creates when
    /// absent; "add" always creates a new instance (idempotent: an identical
    /// instance already present makes it a no-op). "add" forbids "match".</summary>
    public string Mode { get; init; } = "upsert";

    public IReadOnlyList<SubStructure> Substructures { get; init; } = [];
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    /// <summary>Disposition of citations already on the fact when its value is replaced.</summary>
    public string ReplacedCitations { get; init; } = "keep";

    /// <summary>
    /// Vital tags that occur at most once per record, where a no-match op
    /// correctly updates the sole instance in place. Every other tag is treated
    /// as repeatable and gets the no-overwrite guard (see class summary).
    /// </summary>
    private static readonly HashSet<string> SingleValuedFacts = new(StringComparer.OrdinalIgnoreCase)
    { "BIRT", "DEAT", "CHR", "BAPM", "BURI", "CREM", "SEX", "NAME", "MARR", "DIV" };

    internal static CreateOrUpdateVitalOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "createOrUpdateVital"),
        Fact = JsonRead.Req(el, "fact", "createOrUpdateVital"),
        Value = EventValue.Read(el, "value")
                ?? throw new JsonException("createOrUpdateVital: required field 'value' missing"),
        Match = FactMatch.Read(el, "match"),
        Mode = JsonRead.Str(el, "mode") ?? "upsert",
        Substructures = SubStructure.ReadList(el, "substructures"),
        Citations = Citation.ReadAll(el),
        ReplacedCitations = JsonRead.Str(el, "replacedCitations") ?? "keep",
    };

    private string Context => $"{Kind} {Fact} on {Record}";

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Context}: target not in file"); return; }
        OpChecks.CitationsValid(ctx, Context, Citations, errors);
        if (ReplacedCitations is not ("keep" or "drop" or "moveToNote"))
            errors.Add($"{Context}: replacedCitations must be keep, drop, or moveToNote");
        if (Mode is not ("upsert" or "add"))
            errors.Add($"{Context}: mode must be upsert or add");
        if (Mode == "add" && Match is not null)
            errors.Add($"{Context}: mode \"add\" cannot be combined with \"match\" — add always creates a new instance");

        var target = ctx.Existing(Record);
        if (target is null) return;   // created by an earlier op; nothing to inspect yet

        var res = Resolve.Fact(target, Fact, Match);
        if (Mode == "add")
            return;   // add always creates (or no-ops on an identical re-run); no selector to satisfy
        if (res.Ambiguous)
            errors.Add($"{Context}: ambiguous — {res.MatchCount} {Fact} facts match; refine \"match\"");
        else if (res.Fact is null && Match is not null && FactSatisfyingRequest(target) is null)
            errors.Add($"{Context}: no {Fact} matches \"match\"");
        else if (Match is null && res.Fact is not null && !SingleValuedFacts.Contains(Fact)
                 && PendingValueUpdates(res.Fact).Any(u => u.Replaces))
            errors.Add(
                $"{Context}: {Fact} may occur more than once and {res.TagCount} already exist on " +
                $"{Record}; a no-match createOrUpdateVital would overwrite an existing value. Add " +
                $"\"match\" to update a specific instance, or \"mode\":\"add\" to add a new one");
    }

    /// <summary>
    /// A fact instance already in the requested state. When the match
    /// selector misses (it names the pre-update value), a satisfied instance
    /// means a previous run applied this op — the re-run degrades to a no-op
    /// instead of failing, keeping whole-changeset re-runs idempotent.
    /// </summary>
    private GedRecord? FactSatisfyingRequest(GedRecord target) =>
        target.ChildrenByTag(Fact).FirstOrDefault(f =>
            (Value.Text is null || f.Value == GedRecord.EscapeAtSign(Value.Text)) &&
            (Value.Date is null || f.FirstChild("DATE")?.Value == NodeBuilder.PadDay(Value.Date)) &&
            (Value.Place is null || f.FirstChild("PLAC")?.Value == Value.Place));

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[Record];
        var res = Resolve.Fact(target, Fact, Match);
        var existingFact = Mode == "add"
            ? FactSatisfyingRequest(target)   // add: create unless an identical instance is already present (re-run)
            : res.Fact ?? (Match is not null ? FactSatisfyingRequest(target) : null);

        if (existingFact is null)
        {
            var fact = NodeBuilder.EventNode(1, Fact, Value, []);
            foreach (var (sub, i) in Substructures.Select((s, i) => (s, i)))
                NodeBuilder.Attach(fact, NodeBuilder.NewNode(2, sub.Tag, sub.Value), at: i);
            foreach (var cit in Citations)
                NodeBuilder.Attach(fact, NodeBuilder.CitationNode(2, cit));
            NodeBuilder.InsertFact(target, fact);
            state.Mutated();
            state.Touch(target);
            log.Add($"{Context}: created");
            return;
        }

        var updates = PendingValueUpdates(existingFact);
        if (updates.Count > 0)
            NodeBuilder.DisposeCitations(state, target, existingFact, Fact, ReplacedCitations);
        foreach (var update in updates)
        {
            update.Write();
            state.Mutated();
            state.Touch(target);
        }

        var changes = updates.Select(u => u.Description).ToList();
        foreach (var sub in Substructures)
            if (!existingFact.Children.Any(c => c.Tag == sub.Tag && c.Value == sub.Value))
            {
                NodeBuilder.Attach(existingFact, NodeBuilder.NewNode(existingFact.Level + 1, sub.Tag, sub.Value), at: 0);
                state.Mutated();
                state.Touch(target);
                changes.Add($"{sub.Tag} added '{sub.Value}'");
            }
        foreach (var cit in Citations)
            if (NodeBuilder.UpsertCitation(state, existingFact, cit) is string change)
                changes.Add(change);

        log.Add(changes.Count > 0
            ? $"{Context}: updated ({string.Join("; ", changes)})"
            : $"{Context}: no-op (already matches)");
    }

    private sealed record ValueUpdate(string Description, bool Replaces, Action Write);

    /// <summary>
    /// Compare each requested value field against the fact and return the
    /// writes needed, with log descriptions carrying the replaced values.
    /// <c>Replaces</c> is true when the write overwrites an existing non-empty
    /// value (vs. filling a field the fact lacked) — the no-overwrite guard in
    /// <see cref="Validate"/> keys on it for repeatable facts.
    /// </summary>
    private List<ValueUpdate> PendingValueUpdates(GedRecord fact)
    {
        var updates = new List<ValueUpdate>();
        if (Value.Text is not null && fact.Value != GedRecord.EscapeAtSign(Value.Text))
            updates.Add(new($"value '{fact.PayloadValue}' → '{Value.Text}'",
                            !string.IsNullOrEmpty(fact.Value),
                            () => fact.SetValue(GedRecord.EscapeAtSign(Value.Text))));
        if (Value.Date is not null)
        {
            string padded = NodeBuilder.PadDay(Value.Date);
            var date = fact.FirstChild("DATE");
            if (date is null)
                updates.Add(new($"DATE added '{padded}'", false,
                    () => NodeBuilder.InsertEventDetail(fact, NodeBuilder.NewNode(fact.Level + 1, "DATE", padded))));
            else if (date.Value != padded)
                updates.Add(new($"DATE '{date.Value}' → '{padded}'", true, () => date.SetValue(padded)));
        }
        if (Value.Place is not null)
        {
            var place = fact.FirstChild("PLAC");
            if (place is null)
                updates.Add(new($"PLAC added '{Value.Place}'", false,
                    () => NodeBuilder.InsertEventDetail(fact, NodeBuilder.NewNode(fact.Level + 1, "PLAC", Value.Place))));
            else if (place.Value != Value.Place)
                updates.Add(new($"PLAC '{place.Value}' → '{Value.Place}'", true, () => place.SetValue(Value.Place)));
        }
        return updates;
    }
}

/// <summary>
/// Delete for the Vital noun: removes the whole fact subtree. Citations on
/// the fact are dropped by default; "deletedCitations": "moveToNote"
/// preserves them beneath a NOTE on the record. Absent fact → no-op.
/// </summary>
public sealed class DeleteVitalOp : ChangeOp
{
    public override string Kind => "deleteVital";

    public required string Record { get; init; }
    public required string Fact { get; init; }
    public FactMatch? Match { get; init; }
    public string DeletedCitations { get; init; } = "drop";

    internal static DeleteVitalOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "deleteVital"),
        Fact = JsonRead.Req(el, "fact", "deleteVital"),
        Match = FactMatch.Read(el, "match"),
        DeletedCitations = JsonRead.Str(el, "deletedCitations") ?? "drop",
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (!ctx.Known(Record)) { errors.Add($"{Kind} {Fact} on {Record}: target not in file"); return; }
        if (DeletedCitations is not ("drop" or "moveToNote"))
            errors.Add($"{Kind} {Fact} on {Record}: deletedCitations must be drop or moveToNote");
        var target = ctx.Existing(Record);
        if (target is not null && Resolve.Fact(target, Fact, Match).Ambiguous)
            errors.Add($"{Kind} {Fact} on {Record}: ambiguous — multiple {Fact} facts match; refine \"match\"");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[Record];
        var res = Resolve.Fact(target, Fact, Match);
        if (res.Fact is null)
        {
            log.Add($"{Kind} {Fact} on {Record}: no-op (absent)");
            return;
        }

        int cited = res.Fact.ChildrenByTag("SOUR").Count();
        if (DeletedCitations == "moveToNote")
            NodeBuilder.DisposeCitations(state, target, res.Fact, Fact, "moveToNote");
        target.Children.Remove(res.Fact);
        state.Mutated();
        state.Touch(target);
        log.Add($"{Kind} {Fact} on {Record}: deleted" + (cited > 0
            ? DeletedCitations == "moveToNote"
                ? $" ({cited} citation(s) moved to note)"
                : $" ({cited} citation(s) dropped)"
            : ""));
    }
}

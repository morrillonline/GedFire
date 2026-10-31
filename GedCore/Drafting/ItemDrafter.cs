using System.Text.Json.Nodes;
using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>
/// Compiles the findings aimed at one target into a changeset item: one citation per
/// (source, page, grade) with every extract merged into its dataText, the relationship ops that
/// create new people and families, one vital op per distinct fact value, and a verification
/// note on each fact still graded low.
/// </summary>
internal sealed class ItemDrafter(
    SourcePlan sources, IRecordHistory history, VerificationNotePlanner verificationNotes,
    IReadOnlyDictionary<string, DraftPerson> newPeople)
{
    sealed record CiteKey(string Source, string Locator, int Quay);

    sealed record Claim(DraftFinding Finding, CiteKey Cite, string Extract);

    sealed record FactKey(string Record, string Tag, string? Date, string? Place, string? Value, string Substructures, string? ReplacedCitations);

    sealed record Asserted(DraftFinding Finding, DraftFact Fact, CiteKey Cite);

    sealed record AssertedRelation(DraftFinding Finding, DraftRelation Relation, CiteKey Cite);

    // Where a vital op lands among the instances the record already holds of its tag.
    sealed record Placement(JsonObject? Match, bool Add, ExistingFact? Instance);

    readonly HashSet<string> _plannedFamilies = new(StringComparer.Ordinal);

    // Facts whose citation was not attached because their approximate date does not fit the one chosen.
    readonly HashSet<Asserted> _excluded = [];

    public JsonObject Draft(int number, IReadOnlyList<DraftFinding> group, List<DraftDecision> decisions, List<string> errors)
    {
        var withdrawals = group.SelectMany(f => f.Facts.Where(x => x.Withdraw is not null).Select(x => (Finding: f, Fact: x))).ToList();
        var asserted = group.SelectMany(f => f.Facts.Where(x => x.Withdraw is null).Select(x => new Asserted(f, x, new CiteKey(sources.RefFor(f), f.Source.Locator, x.Quay)))).ToList();
        var noteCites = group.SelectMany(f => f.Notes.SelectMany(n => n.Cites.Select(c => NoteClaim(group, f, c)))).ToList();
        var relations = group.SelectMany(f => f.Relations.Where(r => !r.IsRemoval).Select(r => new AssertedRelation(f, r, new CiteKey(sources.RefFor(f), f.Source.Locator, r.Quay)))).ToList();
        var claims = asserted.Select(a => new Claim(a.Finding, a.Cite, a.Fact.Excerpt ?? a.Finding.Extract))
            .Concat(relations.Select(r => new Claim(r.Finding, r.Cite, r.Relation.Excerpt ?? r.Finding.Extract)))
            .Concat(noteCites)
            .ToList();
        var citations = BuildCitations(claims, decisions, errors);

        var ops = new JsonArray();
        var vitals = VitalOps([.. asserted.Where(a => !a.Fact.CiteOnly)], citations, decisions).ToList();
        var sexCorrections = vitals.Where(op => (string?)op["fact"] == "SEX"
            && !Placeholder.IsPlaceholder((string)op["record"]!)).ToHashSet();
        foreach (var correction in vitals.Where(sexCorrections.Contains)) ops.Add(correction);
        foreach (var link in relations.GroupBy(r => (r.Relation.Kind, r.Relation.Person, r.Relation.Other, r.Relation.Family, r.Relation.Role)))
            ops.Add(RelationOp([.. link], citations, decisions));
        foreach (var removal in RemovalOps(group, decisions)) ops.Add(removal);
        foreach (var withdrawal in WithdrawalOps(withdrawals, group, decisions)) ops.Add(withdrawal);
        foreach (var vital in vitals.Where(op => !sexCorrections.Contains(op))) ops.Add(vital);
        foreach (var citation in CitationOps([.. asserted.Where(a => a.Fact.CiteOnly)], citations)) ops.Add(citation);
        foreach (var finding in group)
            foreach (var note in finding.Notes) ops.Add(NoteOp(note, group, finding, citations));
        var noted = asserted.Where(a => !_excluded.Contains(a)).Select(a => new CitedFact(a.Finding, a.Fact, a.Cite.Source)).ToList();
        foreach (var note in verificationNotes.Plan(noted, [.. group.SelectMany(f => f.Notes)], decisions)) ops.Add(note);

        return new JsonObject { ["item"] = number, ["target"] = Label(group[0].Target), ["ops"] = ops };
    }

    Claim NoteClaim(IReadOnlyList<DraftFinding> group, DraftFinding owner, DraftNoteCitation cite)
    {
        var cited = cite.Finding is null ? owner : group.First(f => f.Id == cite.Finding);
        return new Claim(cited, new CiteKey(sources.RefFor(cited), cited.Source.Locator, cite.Quay), cite.Excerpt ?? cited.Extract);
    }

    JsonObject NoteOp(DraftNote note, IReadOnlyList<DraftFinding> group, DraftFinding owner, Dictionary<CiteKey, JsonObject> citations)
    {
        var op = new JsonObject { ["op"] = "createOrUpdateNote", ["record"] = note.Record, ["text"] = note.Text };
        if (note.Match is not null) op["match"] = note.Match;
        var cites = note.Cites.Select(c => NoteClaim(group, owner, c).Cite).Distinct().Select(c => JsonNode.Parse(citations[c].ToJsonString())!).ToList();
        AttachCitations(op, cites);
        return Backed(op, note.Cites.Select(c => c.Finding ?? owner.Id));
    }

    // Names the findings an op rests on, so a proposal can present the evidence behind each operation.
    static JsonObject Backed(JsonObject op, IEnumerable<string> findingIds)
    {
        var ids = findingIds.Distinct().ToList();
        if (ids.Count > 0) op["findings"] = new JsonArray([.. ids.Select(id => (JsonNode)id)]);
        return op;
    }

    static void AttachCitations(JsonObject op, List<JsonNode> cites)
    {
        if (cites.Count == 1) op["citation"] = cites[0];
        else if (cites.Count > 1) op["citations"] = new JsonArray([.. cites]);
    }

    static string Label(DraftTarget target) =>
        string.Join(' ', new[] { target.Xref, target.Name, target.Card }.Where(p => p is not null));

    Dictionary<CiteKey, JsonObject> BuildCitations(List<Claim> claims, List<DraftDecision> decisions, List<string> errors)
    {
        var result = new Dictionary<CiteKey, JsonObject>();
        foreach (var byKey in claims.GroupBy(c => c.Cite))
        {
            var extracts = byKey.Select(c => c.Extract).Distinct().ToList();
            if (extracts.Count > 1)
                decisions.Add(new DraftDecision(DraftDecisionKinds.MergedCitation,
                    $"{extracts.Count} extracts merged into one dataText for {byKey.Key.Source}, page \"{byKey.Key.Locator}\"",
                    [.. byKey.Select(c => c.Finding.Id).Distinct()]));

            result[byKey.Key] = new JsonObject
            {
                ["source"] = byKey.Key.Source,
                ["page"] = byKey.Key.Locator,
                ["dataText"] = string.Join(" … ", extracts),
                ["quay"] = byKey.Key.Quay,
            };
        }

        foreach (var clash in result.GroupBy(c => (c.Key.Source, c.Key.Locator, Text: (string)c.Value["dataText"]!)).Where(g => g.Count() > 1))
            errors.Add($"the citations of {clash.Key.Source} on page \"{clash.Key.Locator}\" carry the same extract at different grades; give each differently graded fact its own excerpt or locator.");
        return result;
    }

    // Links come out after the ones the item adds, so a moved child is in its new family before it leaves the old one.
    // A removal op carries no citation; a plain unlink is flagged so the author records the evidence in a cited note.
    static IEnumerable<JsonObject> RemovalOps(IReadOnlyList<DraftFinding> group, List<DraftDecision> decisions)
    {
        var removals = new Dictionary<string, (JsonObject Op, List<string> Findings)>();
        foreach (var finding in group)
            foreach (var relation in finding.Relations.Where(r => r.IsRemoval || r.From is not null))
            {
                var op = RemovalOp(relation);
                if (removals.TryGetValue(op.ToJsonString(), out var known))
                {
                    known.Findings.Add(finding.Id);
                    continue;
                }

                removals[op.ToJsonString()] = (op, [finding.Id]);
                if (relation.IsRemoval)
                    decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                        $"{DescribeRemoval(relation)} is not cited; record the evidence in a note that cites the finding", [finding.Id]));
            }
        return removals.Values.Select(r => Backed(r.Op, r.Findings));
    }

    static JsonObject RemovalOp(DraftRelation relation)
    {
        if (relation.Kind != DraftRelation.UnlinkParent)
            return new JsonObject { ["op"] = "deleteChild", ["family"] = relation.Kind == DraftRelation.UnlinkChild ? relation.Family : relation.From, ["child"] = relation.Other };

        var op = new JsonObject { ["op"] = "deleteParent", ["person"] = relation.Person, ["role"] = relation.Role };
        if (relation.Family is not null) op["family"] = relation.Family;
        return op;
    }

    static string DescribeRemoval(DraftRelation relation) => relation.Kind == DraftRelation.UnlinkParent
        ? $"removing the {relation.Role} of {relation.Person}"
        : $"removing {relation.Other} from {relation.Family}";

    // A withdrawn fact is deleted with its citations kept in a note; like a removal, its evidence belongs in a cited note.
    IEnumerable<JsonObject> WithdrawalOps(
        List<(DraftFinding Finding, DraftFact Fact)> withdrawals, IReadOnlyList<DraftFinding> group, List<DraftDecision> decisions)
    {
        foreach (var byFact in withdrawals.GroupBy(w => (w.Fact.Record, w.Fact.Tag, Date: w.Fact.Date?.ToUpperInvariant(), w.Fact.Place)))
        {
            var (record, tag, date, place) = byFact.Key;
            List<string> findingIds = [.. byFact.Select(w => w.Finding.Id).Distinct()];
            string selector = date is null && place is null ? "" : $" ({Describe(date, place, null)})";
            var held = history.FactsOn(record, tag)
                .Where(e => (date is null || e.Date is not null && Specificity.SameDate(e.Date, date))
                         && (place is null || e.Place is not null && Specificity.SamePlace(e.Place, place)))
                .ToList();
            if (held.Count != 1)
            {
                decisions.Add(new DraftDecision(DraftDecisionKinds.Open, held.Count == 0
                    ? $"{record} holds no {tag}{selector} to withdraw; nothing was drafted for it"
                    : $"{held.Count} {tag} facts on {record} match{selector}; give the date or place of the one to withdraw", findingIds));
                continue;
            }

            var op = new JsonObject { ["op"] = "deleteVital", ["record"] = record, ["fact"] = tag };
            if (held[0].Date is not null || held[0].Place is not null)
            {
                var match = new JsonObject();
                if (held[0].Date is not null) match["date"] = held[0].Date;
                if (held[0].Place is not null) match["place"] = held[0].Place;
                op["match"] = match;
            }
            op["deletedCitations"] = "moveToNote";

            string reasons = string.Join("; ", byFact.Select(w => w.Fact.Withdraw).Distinct());
            decisions.Add(new DraftDecision(DraftDecisionKinds.Resolved,
                $"withdrawn: {tag} on {record} ({Describe(held[0].Date, held[0].Place, null)}), {reasons}", findingIds));
            if (!group.Any(f => f.Notes.Any(n => n.Record == record)))
                decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                    $"withdrawing {tag} on {record} is not cited; record the evidence in a note that cites the finding", findingIds));
            yield return Backed(op, findingIds);
        }
    }

    // Findings that state the same link share one op, citing each of them.
    JsonObject RelationOp(List<AssertedRelation> link, Dictionary<CiteKey, JsonObject> citations, List<DraftDecision> decisions)
    {
        var relation = link[0].Relation with
        {
            Husb = link.Select(l => l.Relation.Husb).FirstOrDefault(x => x is not null),
            Wife = link.Select(l => l.Relation.Wife).FirstOrDefault(x => x is not null),
            NotDuplicateOf = [.. link.SelectMany(l => l.Relation.NotDuplicateOf).Distinct()],
        };
        var op = relation.Kind switch
        {
            DraftRelation.Spouse => new JsonObject
            {
                ["op"] = "createOrUpdateSpouse", ["person"] = relation.Person, ["spouse"] = PersonRef(relation.Other),
            },
            DraftRelation.Child => new JsonObject
            {
                ["op"] = "createOrUpdateChild", ["family"] = relation.Family, ["child"] = PersonRef(relation.Other),
            },
            _ => new JsonObject
            {
                ["op"] = "createOrUpdateParent", ["person"] = relation.Person, ["role"] = relation.Role, ["parent"] = PersonRef(relation.Other),
            },
        };

        if (relation.Kind == DraftRelation.Child) AddFamilySeed(op, relation, link[0].Finding, decisions);
        else if (relation.Family is not null) op["family"] = relation.Family;
        if (relation.Family is not null && Placeholder.IsPlaceholder(relation.Family)) _plannedFamilies.Add(relation.Family);

        if (relation.NotDuplicateOf.Count > 0) op["notDuplicateOf"] = new JsonArray([.. relation.NotDuplicateOf.Select(x => (JsonNode)x)]);
        AttachCitations(op, [.. link.Select(l => l.Cite).Distinct().Select(c => JsonNode.Parse(citations[c].ToJsonString())!)]);
        return Backed(op, link.Select(l => l.Finding.Id));
    }

    // HUSB/WIFE seed a family only at its creation; a later op on the same family must not repeat them.
    void AddFamilySeed(JsonObject op, DraftRelation relation, DraftFinding finding, List<DraftDecision> decisions)
    {
        bool creates = Placeholder.IsPlaceholder(relation.Family) && !_plannedFamilies.Contains(relation.Family!);
        if (creates)
        {
            if (relation.Husb is not null) op["husb"] = relation.Husb;
            if (relation.Wife is not null) op["wife"] = relation.Wife;
        }
        else if (relation.Husb is not null || relation.Wife is not null)
            decisions.Add(new DraftDecision(DraftDecisionKinds.DroppedSeed,
                $"{relation.Family} already exists or is created by an earlier op, so husb/wife were left off the child op for {relation.Other}", [finding.Id]));
        op["family"] = relation.Family;
    }

    // A person the changeset creates is described inline each time; the dialect lets repeated descriptions agree.
    JsonNode PersonRef(string xref)
    {
        if (!newPeople.TryGetValue(xref, out var person)) return xref;
        var inline = new JsonObject { ["xref"] = xref, ["name"] = person.Name };
        if (person.Sex is not null) inline["sex"] = person.Sex;
        return inline;
    }

    IEnumerable<JsonObject> VitalOps(List<Asserted> asserted, Dictionary<CiteKey, JsonObject> citations, List<DraftDecision> decisions)
    {
        foreach (var byTag in asserted.GroupBy(a => (a.Fact.Record, a.Fact.Tag)))
        {
            bool repeatable = !CreateOrUpdateVitalOp.IsSingleValued(byTag.Key.Tag);
            var existing = history.FactsOn(byTag.Key.Record, byTag.Key.Tag);
            if (!repeatable)
            {
                if (SingleValuedOp(byTag.Key.Record, byTag.Key.Tag, [.. byTag], existing, citations, decisions) is { } single) yield return single;
                continue;
            }

            var values = byTag.GroupBy(a => FactKeyOf(a.Fact)).ToList();
            if (byTag.Any(a => a.Fact.ReplaceHeld is not null))
                decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                    $"replaceHeld applies to single-valued facts; {byTag.Key.Tag} on {byTag.Key.Record} may occur more than once, so it was ignored",
                    [.. byTag.Select(a => a.Finding.Id).Distinct()]));
            foreach (var value in values)
            {
                var placement = Place(repeatable, values.Count, existing, value.Key);
                yield return VitalOp(value.Key, [.. value], placement, citations, decisions);
            }
        }
    }

    // A single-valued fact is written once. Findings that differ only in how much they say are reconciled (see
    // SingleValuedFactResolver); values that disagree, or text facts such as a NAME, are left open for the author.
    JsonObject? SingleValuedOp(
        string record, string tag, List<Asserted> facts, IReadOnlyList<ExistingFact> existing,
        Dictionary<CiteKey, JsonObject> citations, List<DraftDecision> decisions)
    {
        var findingIds = facts.Select(a => a.Finding.Id).Distinct().ToList();
        var keys = facts.Select(a => FactKeyOf(a.Fact)).Distinct().ToList();
        bool reconcilable = (facts.All(a => a.Fact.Value is null) || facts.All(a => a.Fact.Value is not null))
            && keys.Select(k => (k.Substructures, k.ReplacedCitations)).Distinct().Count() == 1;
        if (!reconcilable)
        {
            if (keys.Count > 1)
            {
                decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                    $"{tag} on {record} is given {keys.Count} different values; choose one, nothing was drafted for it", findingIds));
                return null;
            }
            return VitalOp(keys[0], facts, new Placement(null, false, existing.FirstOrDefault()), citations, decisions);
        }

        var claims = facts.Select(a => new FactClaim(
            a.Finding.Id, a.Fact.Date, a.Fact.Place, a.Fact.Quay,
            DraftChecks.Ceiling(a.Fact.Class ?? a.Finding.Source.Class, a.Fact.Information ?? a.Finding.Source.Information), a.Fact.Value)).ToList();
        var resolution = SingleValuedFactResolver.Resolve(claims);
        if (resolution.Conflict)
        {
            decisions.Add(new DraftDecision(DraftDecisionKinds.Open, ConflictDetail(record, tag, claims, resolution.Recommended), findingIds));
            return null;
        }

        string? date = resolution.CombinedDate ?? (resolution.DateFrom is { } d ? claims[d].Date : null);
        string? place = resolution.PlaceFrom is { } p ? claims[p].Place : null;
        string? name = resolution.ValueFrom is { } v ? claims[v].Value : null;
        var held = existing.FirstOrDefault();
        var attached = facts.Where((_, i) => !resolution.Excluded.Contains(i)).ToList();
        string? replaceReason = attached.Select(a => a.Fact.ReplaceHeld).FirstOrDefault(r => r is not null);

        var judged = date is null ? null : JudgeHeldDate(record, tag, held, date, attached.Max(a => a.Fact.Quay), replaceReason);
        if (judged?.Decision is { } judgement)
            decisions.Add(judgement with { Findings = [.. attached.Select(a => a.Finding.Id).Distinct()] });
        if (judged?.Outcome == HeldOutcome.Skip)
        {
            foreach (var skipped in facts) _excluded.Add(skipped);
            return null;
        }

        bool writesPlace = place is not null
            && (replaceReason is not null || !HeldIsMoreSpecific(held?.Place, place, Specificity.PlaceSupersedes));
        // The date the record already holds is not rewritten just to accompany a place the record keeps.
        bool writesDate = judged?.Outcome == HeldOutcome.Write
            && !(place is not null && !writesPlace && held?.Date is { } heldDate && date is not null && Specificity.SameDate(heldDate, date));
        date = judged?.Date ?? date;
        bool replacesHeld = judged?.Replaces == true;

        foreach (int i in resolution.Excluded)
            decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                $"{claims[i].FindingId}'s {claims[i].Date} for {tag} on {record} does not fit the chosen date {date}, so its citation was not attached; check it",
                [claims[i].FindingId]));
        foreach (var dropped in facts.Where((_, i) => resolution.Excluded.Contains(i))) _excluded.Add(dropped);

        if (claims.Select(c => (c.Date, c.Place, c.Value)).Distinct().Count() > 1 || !writesDate && date is not null || !writesPlace && place is not null)
            decisions.Add(new DraftDecision(DraftDecisionKinds.Resolved,
                ResolvedDetail(record, tag, claims, resolution, date, place, name, writesDate, writesPlace, attached), [.. attached.Select(a => a.Finding.Id).Distinct()]));

        if (!writesDate && !writesPlace && name is null) return CitationOnlyOp(record, tag, held, attached, citations, decisions);

        var key = keys[0] with
        {
            Date = writesDate ? date!.ToUpperInvariant() : null, Place = writesPlace ? place : null, Value = name,
            ReplacedCitations = keys[0].ReplacedCitations ?? (replacesHeld ? "moveToNote" : null),
        };
        // The citations of a contradicted value are not restated onto the value that replaces it.
        return VitalOp(key, attached, new Placement(null, false, replacesHeld ? null : held), citations, decisions);
    }

    enum HeldOutcome { Write, Keep, Skip }

    // Date is what to write when it differs from the finding's (a combined bound); Replaces marks a contradicted value being replaced.
    sealed record HeldDate(HeldOutcome Outcome, string? Date = null, bool Replaces = false, DraftDecision? Decision = null);

    // Whether the finding's date is written over the date the record holds. Precision decides only between dates that can
    // both be true; a contradiction is settled by the evidence (an uncited or lower-graded value gives way) or left open.
    static HeldDate JudgeHeldDate(string record, string tag, ExistingFact? heldFact, string date, int quay, string? replaceReason)
    {
        string? held = heldFact?.Date;
        if (held is null || Specificity.SameDate(held, date)) return new HeldDate(HeldOutcome.Write);
        string fact = $"{tag} on {record}";

        if (replaceReason is not null)
            return new HeldDate(HeldOutcome.Write, Replaces: !Specificity.Consistent(held, date),
                Decision: Resolved($"{fact}: the record's {held} replaced with {date} as the author directs, {replaceReason}"));

        if (Specificity.IsBound(held) && Specificity.IsBound(date))
            return Specificity.CombineBounds(held, date, out var combined) switch
            {
                Specificity.BoundOverlap.Combined when Specificity.SameDate(combined!, held) => new HeldDate(HeldOutcome.Keep),
                Specificity.BoundOverlap.Combined => new HeldDate(HeldOutcome.Write, combined,
                    Decision: Resolved($"{fact}: the record's {held} and the finding's {date} together give {combined}")),
                Specificity.BoundOverlap.Disjoint => Contradiction(fact, heldFact!, date, quay),
                _ => new HeldDate(HeldOutcome.Keep,
                    Decision: Open($"{fact}: cannot tell how the finding's {date} narrows the record's {held}; the record's date is kept, check it")),
            };

        if (Specificity.DateSupersedes(held, date))
        {
            if (!Specificity.IsApproximate(date) || Specificity.Fits(date, held)) return new HeldDate(HeldOutcome.Keep);
            if (Specificity.IsBound(date)) return Contradiction(fact, heldFact!, date, quay);
            return new HeldDate(HeldOutcome.Skip, Decision: Open(
                $"{fact}: the finding's {date} is more than a year from the record's {held}, so nothing was drafted for it; check it, then cite or replace the record's date"));
        }

        if (!Specificity.IsApproximate(date) && !Specificity.Fits(held, date))
        {
            if (Specificity.IsBound(held)) return Contradiction(fact, heldFact!, date, quay);
            if (Specificity.IsSoft(held))
                return new HeldDate(HeldOutcome.Write, Decision: Open(
                    $"{fact}: the record's {held} is replaced with {date}, more than a year from it; check that both describe the same event"));
        }
        return new HeldDate(HeldOutcome.Write);
    }

    static HeldDate Contradiction(string fact, ExistingFact held, string date, int quay)
    {
        var grades = held.Citations.Select(c => c.Quay).ToList();
        string standing = grades.Count == 0 ? "uncited" : grades.All(g => g is not null) ? $"cited at QUAY {grades.Max()}" : "cited without a grade";
        bool givesWay = grades.Count == 0 || grades.All(g => g is not null) && grades.Max() < quay;
        return givesWay
            ? new HeldDate(HeldOutcome.Write, Replaces: true, Decision: Resolved(
                $"{fact}: the record's {held.Date} does not fit {date} and is {standing}, below the finding's QUAY {quay}, so it is replaced"
                + (grades.Count == 0 ? "" : " and its citations kept in a note")))
            : new HeldDate(HeldOutcome.Skip, Decision: Open(
                $"{fact}: the record's {held.Date} ({standing}) does not fit {date} (QUAY {quay}); nothing was drafted for it. Settle the conflict, then give the fact replaceHeld with the reason"));
    }

    static DraftDecision Resolved(string detail) => new(DraftDecisionKinds.Resolved, detail, []);

    static DraftDecision Open(string detail) => new(DraftDecisionKinds.Open, detail, []);

    // The record's own value is kept when it says as much as the finding, even spelled differently; only an identical value is rewritten as is.
    static bool HeldIsMoreSpecific(string? held, string given, Func<string, string, bool> supersedes) =>
        held is not null && supersedes(held, given) && held != given;

    JsonObject CitationOnlyOp(
        string record, string tag, ExistingFact? held, List<Asserted> attached,
        Dictionary<CiteKey, JsonObject> citations, List<DraftDecision> decisions)
    {
        var op = new JsonObject { ["op"] = "createOrUpdateCitation", ["record"] = record, ["fact"] = tag };
        if (held is not null && (held.Date is not null || held.Place is not null))
        {
            var match = new JsonObject();
            if (held.Date is not null) match["date"] = held.Date;
            if (held.Place is not null) match["place"] = held.Place;
            op["match"] = match;
        }

        var cites = attached.Select(a => a.Cite).Distinct().Select(c => JsonNode.Parse(citations[c].ToJsonString())!).ToList();
        if (held is not null)
            cites.AddRange(RestatedCitations(
                new FactKey(record, tag, null, null, null, "", null), held, cites, [.. attached.Select(a => a.Finding.Id).Distinct()], decisions));
        AttachCitations(op, cites);
        return Backed(op, attached.Select(a => a.Finding.Id));
    }

    static string ConflictDetail(string record, string tag, List<FactClaim> claims, int recommended)
    {
        var values = claims.GroupBy(c => (c.Date, c.Place, c.Value)).Select(g => $"{Describe(g.Key.Date, g.Key.Place, g.Key.Value)} ({string.Join(", ", g.Select(c => c.FindingId))}; QUAY {g.Max(c => c.Quay)})");
        var best = claims[recommended];
        return $"{tag} on {record} is given {claims.Select(c => (c.Date, c.Place, c.Value)).Distinct().Count()} different values: {string.Join("; ", values)}; " +
               $"recommended {best.FindingId} ({Describe(best.Date, best.Place, best.Value)}, QUAY {best.Quay}); nothing was drafted for it, choose one";
    }

    static string ResolvedDetail(
        string record, string tag, List<FactClaim> claims, FactResolution resolution, string? date, string? place, string? name,
        bool writesDate, bool writesPlace, List<Asserted> attached)
    {
        string kept = string.Join(", ", new[]
        {
            Source("date", resolution.DateFrom, claims), Source("place", resolution.PlaceFrom, claims), Source("name", resolution.ValueFrom, claims),
            resolution.CombinedDate is null ? null : "the findings' bounds combined for the date",
        }.OfType<string>());
        string cited = string.Join(", ", attached.Select(a => a.Finding.Id).Distinct());
        string improvement = !writesDate && !writesPlace && name is null
            ? "; no improvement, the record already holds as much, so only the citations are added"
            : date is not null && !writesDate || place is not null && !writesPlace
                ? "; the record already holds a more specific " + (date is not null && !writesDate ? "date" : "place") + ", which is kept"
                : "";
        return $"resolved: {tag} on {record}, kept {kept} ({Describe(date, place, name)}), cited {cited}{improvement}";
    }

    static string? Source(string what, int? index, List<FactClaim> claims) =>
        index is { } i ? $"{claims[i].FindingId} for the {what} (QUAY {claims[i].Quay})" : null;

    static string Describe(string? date, string? place, string? value) =>
        value ?? (date is not null && place is not null ? $"{date} at {place}" : date ?? place ?? "no value");

    static FactKey FactKeyOf(DraftFact fact) =>
        new(fact.Record, fact.Tag, fact.Date?.ToUpperInvariant(), fact.Place, fact.Value,
            string.Join('|', fact.Substructures.Select(s => $"{s.Tag}={s.Value}")), fact.ReplacedCitations);

    // A repeatable tag never overwrites by accident: a date the record already holds is addressed by match, anything else is added.
    // A fact with substructures is always added: the existing instance's substructures are not known, and an identical one is a no-op.
    static Placement Place(bool repeatable, int valueCount, IReadOnlyList<ExistingFact> existing, FactKey key)
    {
        if (!repeatable) return new Placement(null, false, existing.FirstOrDefault());
        var sameDate = key.Date is null || key.Substructures.Length > 0 ? null : existing.FirstOrDefault(e => string.Equals(e.Date, key.Date, StringComparison.OrdinalIgnoreCase));
        if (sameDate is not null) return new Placement(new JsonObject { ["date"] = key.Date }, false, sameDate);
        return new Placement(null, valueCount > 1 || existing.Count > 0, null);
    }

    JsonObject VitalOp(FactKey key, List<Asserted> facts, Placement placement, Dictionary<CiteKey, JsonObject> citations, List<DraftDecision> decisions)
    {
        var cites = facts.Select(a => a.Cite).Distinct().Select(c => JsonNode.Parse(citations[c].ToJsonString())!).ToList();
        if (placement.Instance is { } instance)
            cites.AddRange(RestatedCitations(key, instance, cites, [.. facts.Select(a => a.Finding.Id).Distinct()], decisions));

        var op = new JsonObject
        {
            ["op"] = "createOrUpdateVital", ["record"] = key.Record, ["fact"] = key.Tag, ["value"] = ValueOf(key),
        };
        if (placement.Match is not null) op["match"] = placement.Match;
        if (placement.Add) op["mode"] = "add";
        if (facts[0].Fact.Substructures.Count > 0)
            op["substructures"] = new JsonArray([.. facts[0].Fact.Substructures.Select(s => (JsonNode)new JsonObject { ["tag"] = s.Tag, ["value"] = s.Value })]);
        if (key.ReplacedCitations is { } disposition and not "keep") op["replacedCitations"] = disposition;
        AttachCitations(op, cites);
        return Backed(op, facts.Select(a => a.Finding.Id));
    }

    // Citations added to facts the record already holds, one op per fact instance.
    IEnumerable<JsonObject> CitationOps(List<Asserted> asserted, Dictionary<CiteKey, JsonObject> citations)
    {
        foreach (var byFact in asserted.GroupBy(a => (a.Fact.Record, a.Fact.Tag, Date: a.Fact.Date?.ToUpperInvariant(), a.Fact.Place)))
        {
            var op = new JsonObject { ["op"] = "createOrUpdateCitation", ["record"] = byFact.Key.Record, ["fact"] = byFact.Key.Tag };
            if (byFact.Key.Date is not null || byFact.Key.Place is not null)
            {
                var match = new JsonObject();
                if (byFact.Key.Date is not null) match["date"] = byFact.Key.Date;
                if (byFact.Key.Place is not null) match["place"] = byFact.Key.Place;
                op["match"] = match;
            }
            AttachCitations(op, [.. byFact.Select(a => a.Cite).Distinct().Select(c => JsonNode.Parse(citations[c].ToJsonString())!)]);
            yield return Backed(op, byFact.Select(a => a.Finding.Id));
        }
    }

    static JsonNode ValueOf(FactKey key)
    {
        if (key.Value is not null) return key.Value;
        var value = new JsonObject();
        if (key.Date is not null) value["date"] = key.Date;
        if (key.Place is not null) value["place"] = key.Place;
        return value;
    }

    // A page stated for a source the fact already cites replaces the old page unless the old citation is stated again.
    static IEnumerable<JsonNode> RestatedCitations(FactKey fact, ExistingFact instance, List<JsonNode> drafted, List<string> findings, List<DraftDecision> decisions)
    {
        foreach (var existing in instance.Citations)
        {
            bool sameSource = drafted.Any(d => (string?)d["source"] == existing.Source);
            bool pageStated = drafted.Any(d => (string?)d["source"] == existing.Source && (string?)d["page"] == existing.Page);
            if (!sameSource || pageStated) continue;

            decisions.Add(new DraftDecision(DraftDecisionKinds.KeptCitation,
                $"{fact.Tag} on {fact.Record} already cites {existing.Source} at \"{existing.Page}\"; stated again so the new page does not replace it", findings));
            var restated = new JsonObject { ["source"] = existing.Source };
            if (existing.Page is not null) restated["page"] = existing.Page;
            if (existing.DataText is not null) restated["dataText"] = existing.DataText;
            if (existing.Quay is { } quay) restated["quay"] = quay;
            yield return restated;
        }
    }
}

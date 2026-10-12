using System.Text.Json.Nodes;
using GedCore.Apply;
using GedCore.Policy;

namespace GedCore.Drafting;

/// <summary>
/// Compiles the findings aimed at one target into a changeset item: one citation per
/// (source, page, grade) with every extract merged into its dataText, the relationship ops that
/// create new people and families, one vital op per distinct fact value, and a verification
/// note on each record holding a low-graded fact.
/// </summary>
internal sealed class ItemDrafter(
    SourcePlan sources, IPersonNames names, IRecordHistory history, VerificationNoteRule verification,
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

    public JsonObject Draft(int number, IReadOnlyList<DraftFinding> group, List<DraftDecision> decisions, List<string> errors)
    {
        var asserted = group.SelectMany(f => f.Facts.Select(x => new Asserted(f, x, new CiteKey(sources.RefFor(f), f.Source.Locator, x.Quay)))).ToList();
        var noteCites = group.SelectMany(f => f.Notes.SelectMany(n => n.Cites.Select(c => NoteClaim(group, f, c)))).ToList();
        var relations = group.SelectMany(f => f.Relations.Select(r => new AssertedRelation(f, r, new CiteKey(sources.RefFor(f), f.Source.Locator, r.Quay)))).ToList();
        var claims = asserted.Select(a => new Claim(a.Finding, a.Cite, a.Fact.Excerpt ?? a.Finding.Extract))
            .Concat(relations.Select(r => new Claim(r.Finding, r.Cite, r.Relation.Excerpt ?? r.Finding.Extract)))
            .Concat(noteCites)
            .ToList();
        var citations = BuildCitations(claims, decisions, errors);

        var ops = new JsonArray();
        foreach (var link in relations.GroupBy(r => (r.Relation.Kind, r.Relation.Person, r.Relation.Other, r.Relation.Family, r.Relation.Role)))
            ops.Add(RelationOp([.. link], citations, decisions));
        foreach (var vital in VitalOps([.. asserted.Where(a => !a.Fact.CiteOnly)], citations, decisions)) ops.Add(vital);
        foreach (var citation in CitationOps([.. asserted.Where(a => a.Fact.CiteOnly)], citations)) ops.Add(citation);
        foreach (var finding in group)
            foreach (var note in finding.Notes) ops.Add(NoteOp(note, group, finding, citations));
        foreach (var note in VerificationNotes(asserted, group.SelectMany(f => f.Notes).ToList(), decisions)) ops.Add(note);

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
        return op;
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
            var values = byTag.GroupBy(a => FactKeyOf(a.Fact)).ToList();
            bool repeatable = !CreateOrUpdateVitalOp.IsSingleValued(byTag.Key.Tag);
            if (values.Count > 1 && !repeatable)
            {
                decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                    $"{byTag.Key.Tag} on {byTag.Key.Record} is given {values.Count} different values; choose one, nothing was drafted for it",
                    [.. byTag.Select(a => a.Finding.Id).Distinct()]));
                continue;
            }

            var existing = history.FactsOn(byTag.Key.Record, byTag.Key.Tag);
            foreach (var value in values)
            {
                var placement = Place(repeatable, values.Count, existing, value.Key);
                yield return VitalOp(value.Key, [.. value], placement, citations, decisions);
            }
        }
    }

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
        return op;
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
            yield return op;
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

    IEnumerable<JsonObject> VerificationNotes(List<Asserted> asserted, List<DraftNote> authorNotes, List<DraftDecision> decisions)
    {
        var low = asserted.Where(a => a.Fact.Quay <= verification.MaxQuay).GroupBy(a => a.Fact.Record);
        foreach (var record in low)
        {
            var findings = record.Select(a => a.Finding.Id).Distinct().ToList();
            if (authorNotes.Any(n => n.Record == record.Key && n.Text.Contains(verification.NotePrefix, StringComparison.OrdinalIgnoreCase)))
            {
                decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote, $"the author's own note on {record.Key} already names the low-graded facts", findings));
                continue;
            }

            var target = record.First().Finding.Target;
            string? name = names.PersonName(record.Key)
                ?? (newPeople.TryGetValue(record.Key, out var created) ? PlainName(created.Name) : null)
                ?? (record.Key == target.Xref ? target.Name : null);
            foreach (string existing in history.NotesOn(record.Key).Where(n => n.Contains(verification.NotePrefix, StringComparison.OrdinalIgnoreCase)))
                decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                    $"{record.Key} already has a verification note (\"{existing}\"); a new one beside it may contradict it, so rewrite it with createOrUpdateNote and match if it is stale",
                    findings));

            string text = VerificationNote.Text(
                verification.NotePrefix, record.Select(a => a.Fact.Tag), name,
                record.Select(a => a.Finding.Source.Name), record.Select(a => a.Fact.Information ?? a.Finding.Source.Information));
            decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote, $"{record.Key}: {string.Join(", ", record.Select(a => a.Fact.Tag).Distinct())} graded {verification.MaxQuay} or lower", findings));
            yield return new JsonObject { ["op"] = "createOrUpdateNote", ["record"] = record.Key, ["text"] = text };
        }
    }

    // GEDCOM marks the surname with slashes; prose does not.
    static string PlainName(string name) => string.Join(' ', name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

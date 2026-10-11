using System.Text.Json.Nodes;
using GedCore.Policy;

namespace GedCore.Drafting;

/// <summary>
/// Compiles the findings aimed at one target into a changeset item: one citation per
/// (source, page, grade) with every extract merged into its dataText, one vital op per distinct
/// fact value, and a verification note on each record holding a low-graded fact.
/// </summary>
internal sealed class ItemDrafter(SourcePlan sources, IPersonNames names, IRecordHistory history, VerificationNoteRule verification)
{
    sealed record CiteKey(string Source, string Locator, int Quay);

    sealed record FactKey(string Record, string Tag, string? Date, string? Place);

    sealed record Asserted(DraftFinding Finding, DraftFact Fact, CiteKey Cite);

    public JsonObject Draft(int number, IReadOnlyList<DraftFinding> group, List<DraftDecision> decisions, List<string> errors)
    {
        var asserted = group.SelectMany(f => f.Facts.Select(x => new Asserted(f, x, new CiteKey(sources.RefFor(f), f.Source.Locator, x.Quay)))).ToList();
        var citations = BuildCitations(asserted, decisions, errors);

        var ops = new JsonArray();
        foreach (var vital in VitalOps(asserted, citations, decisions)) ops.Add(vital);
        foreach (var note in group.SelectMany(f => f.Notes))
            ops.Add(new JsonObject { ["op"] = "createOrUpdateNote", ["record"] = note.Record, ["text"] = note.Text });
        foreach (var note in VerificationNotes(asserted, group.SelectMany(f => f.Notes).ToList(), decisions)) ops.Add(note);

        return new JsonObject { ["item"] = number, ["target"] = Label(group[0].Target), ["ops"] = ops };
    }

    static string Label(DraftTarget target) =>
        string.Join(' ', new[] { target.Xref, target.Name, target.Card }.Where(p => p is not null));

    Dictionary<CiteKey, JsonObject> BuildCitations(List<Asserted> asserted, List<DraftDecision> decisions, List<string> errors)
    {
        var result = new Dictionary<CiteKey, JsonObject>();
        foreach (var byKey in asserted.GroupBy(a => a.Cite))
        {
            var extracts = byKey.Select(a => a.Fact.Excerpt ?? a.Finding.Extract).Distinct().ToList();
            if (extracts.Count > 1)
                decisions.Add(new DraftDecision("merged-citation",
                    $"{extracts.Count} extracts merged into one dataText for {byKey.Key.Source}, page \"{byKey.Key.Locator}\"",
                    [.. byKey.Select(a => a.Finding.Id).Distinct()]));

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

    IEnumerable<JsonObject> VitalOps(List<Asserted> asserted, Dictionary<CiteKey, JsonObject> citations, List<DraftDecision> decisions)
    {
        foreach (var byFact in asserted.GroupBy(a => new FactKey(a.Fact.Record, a.Fact.Tag, a.Fact.Date?.ToUpperInvariant(), a.Fact.Place)).GroupBy(g => (g.Key.Record, g.Key.Tag)))
        {
            var values = byFact.ToList();
            if (values.Count > 1)
            {
                decisions.Add(new DraftDecision("open",
                    $"{byFact.Key.Tag} on {byFact.Key.Record} is given {values.Count} different values; choose one, nothing was drafted for it",
                    [.. values.SelectMany(v => v).Select(a => a.Finding.Id).Distinct()]));
                continue;
            }

            var key = values[0].Key;
            var cites = values[0].Select(a => a.Cite).Distinct().Select(c => JsonNode.Parse(citations[c].ToJsonString())!).ToList();
            cites.AddRange(RestatedCitations(key, cites, values[0].Select(a => a.Finding.Id).Distinct().ToList(), decisions));
            var value = new JsonObject();
            if (key.Date is not null) value["date"] = key.Date;
            if (key.Place is not null) value["place"] = key.Place;

            var op = new JsonObject { ["op"] = "createOrUpdateVital", ["record"] = key.Record, ["fact"] = key.Tag, ["value"] = value };
            if (cites.Count == 1) op["citation"] = cites[0];
            else op["citations"] = new JsonArray([.. cites]);
            yield return op;
        }
    }

    // A page stated for a source the fact already cites replaces the old page unless the old citation is stated again.
    IEnumerable<JsonNode> RestatedCitations(FactKey fact, List<JsonNode> drafted, List<string> findings, List<DraftDecision> decisions)
    {
        foreach (var existing in history.CitationsOn(fact.Record, fact.Tag))
        {
            bool sameSource = drafted.Any(d => (string?)d["source"] == existing.Source);
            bool pageStated = drafted.Any(d => (string?)d["source"] == existing.Source && (string?)d["page"] == existing.Page);
            if (!sameSource || pageStated) continue;

            decisions.Add(new DraftDecision("kept-citation",
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
                decisions.Add(new DraftDecision("verification-note", $"the author's own note on {record.Key} already names the low-graded facts", findings));
                continue;
            }

            var target = record.First().Finding.Target;
            string? name = names.PersonName(record.Key) ?? (record.Key == target.Xref ? target.Name : null);
            foreach (string existing in history.NotesOn(record.Key).Where(n => n.Contains(verification.NotePrefix, StringComparison.OrdinalIgnoreCase)))
                decisions.Add(new DraftDecision("open",
                    $"{record.Key} already has a verification note (\"{existing}\"); a new one beside it may contradict it, so rewrite it with createOrUpdateNote and match if it is stale",
                    findings));

            string text = VerificationNote.Text(
                verification.NotePrefix, record.Select(a => a.Fact.Tag), name,
                record.Select(a => a.Finding.Source.Name), record.Select(a => a.Fact.Information ?? a.Finding.Source.Information));
            decisions.Add(new DraftDecision("verification-note", $"{record.Key}: {string.Join(", ", record.Select(a => a.Fact.Tag).Distinct())} graded {verification.MaxQuay} or lower", findings));
            yield return new JsonObject { ["op"] = "createOrUpdateNote", ["record"] = record.Key, ["text"] = text };
        }
    }
}

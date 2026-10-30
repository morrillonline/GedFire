using System.Text.Json.Nodes;
using GedCore.Apply;
using GedCore.Policy;

namespace GedCore.Drafting;

/// <summary>One fact an item asserts, with the reference of the source its citation points at.</summary>
internal sealed record CitedFact(DraftFinding Finding, DraftFact Fact, string SourceRef);

/// <summary>
/// Judges each fact an item asserts on its own: a fact is noted as awaiting verification only when its best citation, in the
/// item or already in the document, is at or below the ceiling, and no note on the record already says so. The note names
/// every record the fact rests on. Notes the compiler wrote earlier are brought up to date as the evidence improves.
/// </summary>
internal sealed class VerificationNotePlanner(
    IPersonNames names, IRecordHistory history, VerificationNoteRule rule, IReadOnlyDictionary<string, DraftPerson> newPeople)
{
    const string UntitledSource = "a source the record already cites";

    sealed record Assessment(string Tag, int Best, bool HasLowCitation, bool Due, IReadOnlyList<string> Records, IReadOnlyList<string> Information);

    // A verification note already on the record; Parsed is null when the compiler did not write it.
    sealed record HeldNote(string Text, ParsedVerificationNote? Parsed, IReadOnlyList<string> StillLow)
    {
        public bool Covers(string tag) => Parsed is null ? VerificationNote.Mentions(Text, tag) : StillLow.Contains(tag);
    }

    public IEnumerable<JsonObject> Plan(IReadOnlyList<CitedFact> facts, IReadOnlyList<DraftNote> authorNotes, List<DraftDecision> decisions) =>
        facts.GroupBy(f => f.Fact.Record).SelectMany(record => PlanRecord(record.Key, [.. record], authorNotes, decisions));

    IEnumerable<JsonObject> PlanRecord(string record, List<CitedFact> facts, IReadOnlyList<DraftNote> authorNotes, List<DraftDecision> decisions)
    {
        var findings = facts.Select(f => f.Finding.Id).Distinct().ToList();
        string? name = NameOf(record, facts[0].Finding.Target);
        var assessments = facts.GroupBy(f => InstanceOf(f.Fact)).Select(g => Assess(record, [.. g])).ToList();
        foreach (var strong in assessments.Where(a => a.HasLowCitation && !a.Due))
            decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote,
                $"{record}: {strong.Tag} is cited at QUAY {strong.Best}, above {rule.MaxQuay}, so it needs no verification note", findings));

        var due = assessments.Where(a => a.Due).ToList();
        var held = HeldNotes(record, name, facts, authorNotes);
        var covered = due.Where(a => held.Any(n => n.Covers(a.Tag))).ToList();
        var toWrite = due.Except(covered).ToList();

        foreach (var op in UpdateStaleNotes(record, name, held, toWrite.Count > 0, findings, decisions)) yield return op;
        if (due.Count == 0) yield break;

        if (authorNotes.Any(n => n.Record == record && n.Text.Contains(rule.NotePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote, $"the author's own note on {record} already names the low-graded facts", findings));
            yield break;
        }

        foreach (var fact in covered)
            decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote,
                $"{record}: a note on the record already says the {fact.Tag} awaits verification, so no second note was added", findings));

        if (toWrite.Count == 0) yield break;
        foreach (var other in held.Where(n => n.Parsed is null && !due.Any(a => n.Covers(a.Tag))))
            decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                $"{record} already has a verification note (\"{other.Text}\"); a new one beside it may contradict it, so rewrite it with createOrUpdateNote and match if it is stale",
                findings));

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in toWrite)
        {
            string text = VerificationNote.Text(rule.NotePrefix, fact.Tag, name, fact.Records, fact.Information);
            if (!written.Add(text)) continue;
            decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote,
                $"{record}: {fact.Tag} rests on {fact.Records.Count} record(s), the best graded QUAY {fact.Best}, {rule.MaxQuay} or lower", findings));
            yield return new JsonObject { ["op"] = "createOrUpdateNote", ["record"] = record, ["text"] = text };
        }
    }

    // A repeatable tag holds one fact per date; a single-valued tag is one fact.
    static (string Tag, string? Date) InstanceOf(DraftFact fact) =>
        (fact.Tag, CreateOrUpdateVitalOp.IsSingleValued(fact.Tag) ? null : fact.Date?.ToUpperInvariant());

    Assessment Assess(string record, List<CitedFact> facts)
    {
        var fact = facts[0].Fact;
        var held = history.FactsOn(record, fact.Tag)
            .Where(e => CreateOrUpdateVitalOp.IsSingleValued(fact.Tag) || string.Equals(e.Date, fact.Date, StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => e.Citations).ToList();
        int best = facts.Select(f => f.Fact.Quay).Concat(held.Select(c => c.Quay).OfType<int>()).Max();
        var cited = facts.Select(f => (Ref: f.SourceRef, Page: (string?)f.Finding.Source.Locator, Name: f.Finding.Source.Name))
            .Concat(held.Select(c => (Ref: c.Source, c.Page, Name: c.Title ?? UntitledSource)))
            .DistinctBy(c => (c.Ref, c.Page))
            .ToList();
        return new Assessment(
            fact.Tag, best, facts.Any(f => f.Fact.Quay <= rule.MaxQuay), best <= rule.MaxQuay, RecordNames(cited),
            [.. facts.Select(f => f.Fact.Information ?? f.Finding.Source.Information)]);
    }

    // A source cited at more than one page is named once per page, so each record is counted.
    static List<string> RecordNames(List<(string Ref, string? Page, string Name)> cited) =>
        [.. cited.GroupBy(c => c.Name)
            .SelectMany(g => g.Count() > 1 ? g.Select(c => c.Page is null ? g.Key : $"{g.Key} ({c.Page})") : [g.Key])
            .Distinct()];

    string? NameOf(string record, DraftTarget target) =>
        names.PersonName(record)
        ?? (newPeople.TryGetValue(record, out var created) ? PlainName(created.Name) : null)
        ?? (record == target.Xref ? target.Name : null);

    List<HeldNote> HeldNotes(string record, string? name, List<CitedFact> facts, IReadOnlyList<DraftNote> authorNotes) =>
        [.. history.NotesOn(record)
            .Where(text => text.Contains(rule.NotePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(text => !authorNotes.Any(n => n.Record == record && n.Match == text))
            .Select(text => VerificationNote.TryParse(text, rule.NotePrefix, name) is { } parsed
                ? new HeldNote(text, parsed, [.. parsed.Tags.Where(t => !IsGradedAboveCeiling(record, t, facts))])
                : new HeldNote(text, null, []))];

    // A note the compiler wrote earlier is rewritten without the facts now graded above the ceiling, or removed when none is left.
    IEnumerable<JsonObject> UpdateStaleNotes(
        string record, string? name, List<HeldNote> held, bool newNoteFollows, List<string> findings, List<DraftDecision> decisions)
    {
        foreach (var note in held.Where(n => n.Parsed is not null))
        {
            var parsed = note.Parsed!;
            var improved = parsed.Tags.Except(note.StillLow).ToList();
            if (improved.Count == 0)
            {
                if (newNoteFollows)
                    decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote,
                        $"{record} keeps its verification note (\"{note.Text}\"): {string.Join(", ", parsed.Tags)} are still graded {rule.MaxQuay} or lower, so a new note beside it does not contradict it",
                        findings));
                continue;
            }

            if (note.StillLow.Count == 0)
            {
                decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote,
                    $"{record}: its verification note (\"{note.Text}\") no longer applies, since {string.Join(", ", improved)} are now graded above {rule.MaxQuay}; removed", findings));
                yield return new JsonObject { ["op"] = "deleteNote", ["record"] = record, ["text"] = note.Text };
                continue;
            }

            decisions.Add(new DraftDecision(DraftDecisionKinds.VerificationNote,
                $"{record}: {string.Join(", ", improved)} are now graded above {rule.MaxQuay}, so its verification note (\"{note.Text}\") now names only {string.Join(", ", note.StillLow)}", findings));
            yield return new JsonObject
            {
                ["op"] = "createOrUpdateNote", ["record"] = record, ["match"] = note.Text,
                ["text"] = VerificationNote.Rewrite(rule.NotePrefix, parsed, note.StillLow, name),
            };
        }
    }

    // The best grade the draft or the record gives the fact; unknown when nothing says.
    bool IsGradedAboveCeiling(string record, string tag, List<CitedFact> facts) =>
        facts.Where(a => a.Fact.Tag == tag).Select(a => a.Fact.Quay)
            .Concat(history.FactsOn(record, tag).SelectMany(f => f.Citations).Select(c => c.Quay).OfType<int>())
            .DefaultIfEmpty(-1).Max() > rule.MaxQuay;

    // GEDCOM marks the surname with slashes; prose does not.
    static string PlainName(string name) => string.Join(' ', name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

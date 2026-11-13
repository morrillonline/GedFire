using System.Text.Json.Nodes;
using GedCore.Policy;

namespace GedCore.Drafting;

public sealed record DraftResult(JsonObject? Changeset, IReadOnlyList<DraftDecision> Decisions, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Compiles findings into a changeset in the op dialect. The mechanical work is done the same way
/// every time (source reuse, merged extracts, verbatim locators, verification notes, QUAY ceilings);
/// the values, identity and note wording are the author's and arrive as input, so nothing is guessed.
/// </summary>
public sealed class ChangesetDrafter(IDraftLookup lookup, ChangesetPolicy? policy = null)
{
    const string DefaultNotePrefix = "needs independent verification:";

    /// <summary>The refusals the findings earn before compiling against the document, without compiling them.</summary>
    /// <param name="complete">False for findings still open: a new person nothing links yet, or a finding with nothing to compile, is then not refused.</param>
    public IReadOnlyList<string> Check(IReadOnlyList<DraftFinding> findings, bool complete = true) => [.. DraftChecks.Check(findings, lookup, complete)];

    /// <summary>
    /// The refusals the findings alone decide, with no document: QUAY ceilings, placeholder declarations, verbatim
    /// excerpts, one grade per quoted text, and the rest. Each is worded exactly as <see cref="Draft"/> words it.
    /// </summary>
    /// <param name="complete">False for findings still open: a new person nothing links yet, or a finding with nothing to compile, is then not refused.</param>
    public static IReadOnlyList<string> CheckWithoutDocument(IReadOnlyList<DraftFinding> findings, bool complete = true) =>
        [.. DraftChecks.Check(findings, null, complete)];

    public DraftResult Draft(IReadOnlyList<DraftFinding> findings)
    {
        var decisions = new List<DraftDecision>();
        var errors = DraftChecks.Check(findings, lookup).ToList();
        if (errors.Count > 0) return new DraftResult(null, decisions, errors);

        var sources = SourcePlan.Build(findings, lookup, decisions);
        var newPeople = findings.SelectMany(f => f.People).DistinctBy(p => p.Xref).ToDictionary(p => p.Xref);
        decisions.AddRange(MintedPeople(findings));
        var verificationNotes = new VerificationNotePlanner(lookup, lookup, policy?.VerificationNote ?? new VerificationNoteRule(1, DefaultNotePrefix), newPeople);
        var itemDrafter = new ItemDrafter(sources, lookup, verificationNotes, newPeople);

        var items = new JsonArray();
        var evidence = new JsonArray();
        foreach (var group in findings.GroupBy(f => (f.Target.Xref, f.Target.Card)))
        {
            int number = items.Count + 1;
            items.Add(itemDrafter.Draft(number, [.. group], decisions, errors));
            foreach (var finding in group) evidence.Add(FindingEvidence.Describe(finding, number, sources.RefFor(finding)));
        }

        var changeset = new JsonObject();
        if (sources.NewSources.Count > 0) changeset["newSources"] = sources.NewSources;
        changeset["items"] = items;
        changeset["findings"] = evidence;

        errors.AddRange(QuayDisagreements(items));
        if (policy is not null) errors.AddRange(policy.Check(changeset.ToJsonString()));
        return new DraftResult(changeset, decisions, errors);
    }

    static IEnumerable<DraftDecision> MintedPeople(IReadOnlyList<DraftFinding> findings) =>
        findings.SelectMany(f => f.People.Select(p => (Finding: f, Person: p)))
            .GroupBy(x => x.Person.Xref)
            .Select(g => new DraftDecision(DraftDecisionKinds.MintedPerson,
                $"{g.Key} for {g.First().Person.Name}, created through a relation",
                [.. g.Select(x => x.Finding.Id).Distinct()]));

    // One (source, page, extract) carries one QUAY everywhere in the changeset.
    static IEnumerable<string> QuayDisagreements(JsonArray items) =>
        items.SelectMany(item => item!["ops"]!.AsArray())
            .Select(op => op!)
            .SelectMany(op => op["citations"]?.AsArray().Select(c => c!) ?? (op["citation"] is { } one ? [one] : []))
            .GroupBy(c => ((string?)c["source"], (string?)c["page"], (string?)c["dataText"]))
            .Where(g => g.Select(c => (int?)c["quay"]).Distinct().Count() > 1)
            .Select(g => $"the citation of {g.Key.Item1} at \"{g.Key.Item2}\" is graded differently in different items; one source, page and extract carries one QUAY, so give the differently graded fact its own excerpt.");
}

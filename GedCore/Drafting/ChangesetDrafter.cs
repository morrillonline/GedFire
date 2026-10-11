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

    public DraftResult Draft(IReadOnlyList<DraftFinding> findings)
    {
        var decisions = new List<DraftDecision>();
        var errors = DraftChecks.Check(findings, lookup).ToList();
        if (errors.Count > 0) return new DraftResult(null, decisions, errors);

        var sources = SourcePlan.Build(findings, lookup, decisions);
        var itemDrafter = new ItemDrafter(sources, lookup, lookup, policy?.VerificationNote ?? new VerificationNoteRule(1, DefaultNotePrefix));

        var items = new JsonArray();
        foreach (var group in findings.GroupBy(f => (f.Target.Xref, f.Target.Card)))
            items.Add(itemDrafter.Draft(items.Count + 1, [.. group], decisions, errors));

        var changeset = new JsonObject();
        if (sources.NewSources.Count > 0) changeset["newSources"] = sources.NewSources;
        changeset["items"] = items;

        errors.AddRange(QuayDisagreements(items));
        if (policy is not null) errors.AddRange(policy.Check(changeset.ToJsonString()));
        return new DraftResult(changeset, decisions, errors);
    }

    // One (source, page, extract) carries one QUAY everywhere in the changeset.
    static IEnumerable<string> QuayDisagreements(JsonArray items) =>
        items.SelectMany(item => item!["ops"]!.AsArray())
            .Select(op => op!)
            .SelectMany(op => op["citations"]?.AsArray().Select(c => c!) ?? (op["citation"] is { } one ? [one] : []))
            .GroupBy(c => ((string?)c["source"], (string?)c["page"], (string?)c["dataText"]))
            .Where(g => g.Select(c => (int?)c["quay"]).Distinct().Count() > 1)
            .Select(g => $"the citation of {g.Key.Item1} at \"{g.Key.Item2}\" is graded differently in different items; one source, page and extract carries one QUAY, so give the differently graded fact its own excerpt.");
}

using System.Text.Json.Nodes;

namespace GedCore.Drafting;

/// <summary>
/// One source reference per collection: an existing source when the document already has one,
/// otherwise a single placeholder minted for every finding of that collection. A finding nothing cites (one that only
/// withdraws or removes) gets no source, so the changeset never mints a source no citation names.
/// </summary>
internal sealed class SourcePlan
{
    // A finding naming its source by xref is keyed by that xref alone; the others by collection and author.
    readonly record struct SourceKey(string? Ref, string Collection, string Author);

    readonly Dictionary<SourceKey, string> _refs = [];

    public JsonArray NewSources { get; } = [];

    public string RefFor(DraftFinding finding) => _refs[KeyOf(finding)];

    public string? CitedRefOf(DraftFinding finding) => _refs.GetValueOrDefault(KeyOf(finding));

    // Findings whose titles differ only in case, punctuation or a trailing person name cite one collection.
    static SourceKey KeyOf(DraftFinding finding) =>
        finding.Source.Ref is { } named
            ? new SourceKey(named, "", "")
            : new SourceKey(null, SourceTitles.Identity(SourceTitles.Collection(finding.Source.Name)), (finding.Source.Author ?? "").ToLowerInvariant());

    static HashSet<string> CitedFindings(IReadOnlyList<DraftFinding> drafted) =>
        [.. drafted.Where(CitesItself).Select(f => f.Id).Concat(drafted.SelectMany(f => f.CitedIds))];

    static bool CitesItself(DraftFinding finding) =>
        finding.Facts.Any(x => x.CitesFinding) || finding.Relations.Any(r => !r.IsRemoval)
        || finding.Notes.Any(n => n.Cites.Any(c => c.Finding is null || c.Finding == finding.Id));

    /// <param name="drafted">The findings drafted into items.</param>
    /// <param name="citedOnly">Findings the drafted ones cite but that are not drafted; they get a source only when cited.</param>
    public static SourcePlan Build(
        IReadOnlyList<DraftFinding> drafted, IReadOnlyList<DraftFinding> citedOnly, ISourceCatalog catalog, List<DraftDecision> decisions)
    {
        var plan = new SourcePlan();
        var cited = CitedFindings(drafted);
        foreach (var collection in drafted.Concat(citedOnly).Where(f => cited.Contains(f.Id)).GroupBy(KeyOf))
        {
            var members = collection.ToList();
            var titles = members.GroupBy(m => SourceTitles.Identity(m.Source.Name), (_, g) => g.ToList()).ToList();

            if (collection.Key.Ref is { } named)
            {
                plan._refs[collection.Key] = named;
                ReportTitles(titles, t => $"title \"{t}\" → citing {named}, the source the finding names", DraftDecisionKinds.ReusedSource, decisions);
                continue;
            }

            if (FindExisting(members, catalog) is { } existing)
            {
                plan._refs[collection.Key] = existing;
                ReportTitles(titles, t => $"title \"{t}\" → reusing {existing}", DraftDecisionKinds.ReusedSource, decisions);
                continue;
            }

            string placeholder = $"@NewSource{plan.NewSources.Count + 1}@";
            string title = titles.Count == 1 ? members[0].Source.Name.Trim() : SourceTitles.Collection(members[0].Source.Name);
            plan._refs[collection.Key] = placeholder;
            plan.NewSources.Add(NewSourceGroup(placeholder, title, members, decisions));
            ReportTitles(titles,
                t => $"title \"{t}\" → minting new source {placeholder} titled \"{title}\", one record for {members.Count} finding(s)",
                DraftDecisionKinds.MintedSource, decisions);
        }
        return plan;
    }

    // An exact title is tried before the bare collection title it would reduce to.
    static string? FindExisting(List<DraftFinding> members, ISourceCatalog catalog)
    {
        var author = members[0].Source.Author;
        var names = members.Select(m => m.Source.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var candidates = names.Concat(names.Select(SourceTitles.Collection)).Distinct(StringComparer.OrdinalIgnoreCase);
        return candidates.Select(c => catalog.FindSource(c, author)).FirstOrDefault(x => x is not null);
    }

    static void ReportTitles(
        List<List<DraftFinding>> titles, Func<string, string> detail, string kind, List<DraftDecision> decisions)
    {
        foreach (var findings in titles)
            decisions.Add(new DraftDecision(kind, detail(findings[0].Source.Name.Trim()), [.. findings.Select(f => f.Id)]));
    }

    static JsonObject NewSourceGroup(string placeholder, string title, List<DraftFinding> members, List<DraftDecision> decisions)
    {
        var op = new JsonObject { ["op"] = "createOrUpdateSource", ["xref"] = placeholder, ["title"] = title };
        if (members[0].Source.Author is { } author) op["auth"] = author;
        AddIfShared(op, "url", members, s => s.Url, decisions);
        AddIfShared(op, "accessed", members, s => s.Accessed, decisions);
        return new JsonObject { ["xref"] = placeholder, ["ops"] = new JsonArray(op) };
    }

    static void AddIfShared(
        JsonObject op, string field, List<DraftFinding> members, Func<DraftSource, string?> read, List<DraftDecision> decisions)
    {
        var values = members.Select(m => read(m.Source)).Where(v => v is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (values.Count == 1) op[field] = values[0];
        else if (values.Count > 1)
            decisions.Add(new DraftDecision(DraftDecisionKinds.Open,
                $"the findings of \"{members[0].Source.Name}\" give {values.Count} different {field} values, so the source record carries none; decide which belongs on it",
                [.. members.Select(m => m.Id)]));
    }
}

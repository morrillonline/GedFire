using System.Text.Json.Nodes;

namespace GedCore.Drafting;

/// <summary>
/// One source reference per collection: an existing source when the document already has one,
/// otherwise a single placeholder minted for every finding of that collection.
/// </summary>
internal sealed class SourcePlan
{
    readonly Dictionary<(string Name, string Author), string> _refs = [];

    public JsonArray NewSources { get; } = [];

    public string RefFor(DraftFinding finding) => _refs[KeyOf(finding)];

    static (string, string) KeyOf(DraftFinding finding) =>
        (finding.Source.Name.ToLowerInvariant(), (finding.Source.Author ?? "").ToLowerInvariant());

    public static SourcePlan Build(IReadOnlyList<DraftFinding> findings, ISourceCatalog catalog, List<DraftDecision> decisions)
    {
        var plan = new SourcePlan();
        foreach (var collection in findings.GroupBy(KeyOf))
        {
            var members = collection.ToList();
            var first = members[0].Source;
            var ids = members.Select(m => m.Id).ToList();

            if (catalog.FindSource(first.Name, first.Author) is { } existing)
            {
                plan._refs[collection.Key] = existing;
                decisions.Add(new DraftDecision("reused-source", $"cite {existing} for \"{first.Name}\"", ids));
                continue;
            }

            string placeholder = $"@NewSource{plan.NewSources.Count + 1}@";
            plan._refs[collection.Key] = placeholder;
            plan.NewSources.Add(NewSourceGroup(placeholder, members, decisions));
            decisions.Add(new DraftDecision("minted-source", $"{placeholder} for \"{first.Name}\", one record for {members.Count} finding(s)", ids));
        }
        return plan;
    }

    static JsonObject NewSourceGroup(string placeholder, List<DraftFinding> members, List<DraftDecision> decisions)
    {
        var op = new JsonObject { ["op"] = "createOrUpdateSource", ["xref"] = placeholder, ["title"] = members[0].Source.Name };
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
            decisions.Add(new DraftDecision("open",
                $"the findings of \"{members[0].Source.Name}\" give {values.Count} different {field} values, so the source record carries none; decide which belongs on it",
                [.. members.Select(m => m.Id)]));
    }
}

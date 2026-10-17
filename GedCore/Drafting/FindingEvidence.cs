using System.Text.Json.Nodes;

namespace GedCore.Drafting;

/// <summary>
/// The changeset's table of findings: each entry holds what a reviewer needs to weigh the finding (its verbatim
/// extract, its source and the source's classes, the grade of each claim, and the identity argument correlation
/// wrote), keyed by the id the ops name in their "findings", so a changeset carries its own evidence.
/// </summary>
internal static class FindingEvidence
{
    public static JsonObject Describe(DraftFinding finding, int item, string sourceRef)
    {
        var claims = finding.Facts.Select(FactClaim).Concat(finding.Relations.Where(r => !r.IsRemoval).Select(RelationClaim));
        var evidence = new JsonObject
        {
            ["id"] = finding.Id,
            ["item"] = item,
            ["extract"] = finding.Extract,
            ["source"] = SourceOf(finding.Source, sourceRef),
            ["claims"] = new JsonArray([.. claims]),
        };
        if (finding.Argument is not null) evidence["argument"] = finding.Argument.DeepClone();
        return evidence;
    }

    static JsonObject SourceOf(DraftSource source, string sourceRef)
    {
        var node = new JsonObject { ["ref"] = sourceRef, ["name"] = source.Name };
        Set(node, "author", source.Author);
        Set(node, "url", source.Url);
        node["locator"] = source.Locator;
        Set(node, "accessed", source.Accessed);
        node["class"] = source.Class;
        node["information"] = source.Information;
        return node;
    }

    static JsonNode FactClaim(DraftFact fact)
    {
        var node = new JsonObject { ["record"] = fact.Record, ["fact"] = fact.Tag };
        Set(node, "date", fact.Date);
        Set(node, "place", fact.Place);
        Set(node, "value", fact.Value);
        node["quay"] = fact.Quay;
        Set(node, "class", fact.Class);
        Set(node, "information", fact.Information);
        return node;
    }

    static JsonNode RelationClaim(DraftRelation relation)
    {
        var node = new JsonObject { ["relation"] = relation.Kind, ["person"] = relation.Person, ["other"] = relation.Other };
        Set(node, "family", relation.Family);
        Set(node, "role", relation.Role);
        node["quay"] = relation.Quay;
        return node;
    }

    static void Set(JsonObject node, string name, string? value)
    {
        if (value is not null) node[name] = value;
    }
}

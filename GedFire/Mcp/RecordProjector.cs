using System.Text.Json;
using System.Text.Json.Nodes;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Narrows a get_record-shaped record to recordType, xref, and the requested
// fields. The accepted field names are read from get_record's own output
// schema so they cannot drift from the record types.
// ---------------------------------------------------------------------------

public static class RecordProjector
{
    static readonly string[] AlwaysPresent = ["recordType", "xref"];
    static readonly string[] ProjectableRecordTypes = ["PersonRecord", "FamilyRecord", "SourceRecord"];

    /// <summary>Every field name any person, family, or source record carries, other than the two always present.</summary>
    public static IReadOnlyList<string> AcceptedFields { get; } = ReadAcceptedFields();

    public static JsonObject Project(object record, IReadOnlySet<string> fields)
    {
        var full = JsonSerializer.SerializeToNode(record, record.GetType(), CallToolResults.JsonOptions)!.AsObject();
        var projected = new JsonObject();
        foreach (var (name, value) in full)
            if (AlwaysPresent.Contains(name) || fields.Contains(name))
                projected[name] = value?.DeepClone();
        return projected;
    }

    static List<string> ReadAcceptedFields()
    {
        var defs = JsonNode.Parse(GetRecordTool.OutputSchemaJson)!["$defs"]!.AsObject();
        return [.. ProjectableRecordTypes
            .SelectMany(type => defs[type]!["properties"]!.AsObject().Select(p => p.Key))
            .Where(name => !AlwaysPresent.Contains(name))
            .Distinct()];
    }
}

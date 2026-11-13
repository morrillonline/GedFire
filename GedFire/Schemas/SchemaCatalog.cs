using System.Text.Json.Nodes;

namespace GedFire.Schemas;

// ---------------------------------------------------------------------------
// The published, versioned JSON Schemas (draft 2020-12) of the finding-file
// contract. A new optional field or enum value bumps a schema's minor
// version; a removal, rename, new required field or tighter constraint bumps
// its major version, and with it the $id.
// ---------------------------------------------------------------------------

/// <summary>One published schema: its name, version, and the document <see cref="SchemaCatalog"/> builds for it.</summary>
public sealed record PublishedSchema(string Name, string Version, string Title, Func<JsonObject> Body)
{
    public string Id => $"{SchemaCatalog.BaseUri}{Name}/{Version.Split('.')[0]}";

    /// <summary>The standalone schema document, self-contained so any off-the-shelf validator can use it.</summary>
    public JsonObject Document()
    {
        var document = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = Id,
            ["title"] = Title,
            ["x-schemaVersion"] = Version,
        };
        foreach (var (key, value) in Body()) document[key] = value?.DeepClone();
        return document;
    }
}

public static class SchemaCatalog
{
    public const string BaseUri = "https://github.com/morrillonline/GedFire/schema/";

    public const string FindingName = "finding";
    public const string FindingFileName = "finding-file";
    public const string RecordName = "record";
    public const string ArgumentName = "argument";

    public static readonly PublishedSchema Finding = new(FindingName, "1.0", "GedFire finding", () => Parse(FindingSchemas.FindingJson));
    public static readonly PublishedSchema Record = new(RecordName, "1.0", "GedFire record", () => Parse(FindingSchemas.RecordJson));
    public static readonly PublishedSchema Argument = new(ArgumentName, "1.0", "GedFire identity argument", () => Parse(FindingSchemas.ArgumentJson));
    public static readonly PublishedSchema FindingFile = new(FindingFileName, "1.0", "GedFire finding file", FindingFileBody);

    public static readonly IReadOnlyList<PublishedSchema> All = [Finding, FindingFile, Record, Argument];

    public static IEnumerable<string> Names => All.Select(s => s.Name);

    public static PublishedSchema? Find(string name) => All.FirstOrDefault(s => s.Name == name);

    static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    static JsonObject FindingFileBody()
    {
        var body = Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "description": "One finding file. GedFire owns every key but meta, which is the caller's and never read.",
              "properties": {
                "draft": { "anyOf": [{ "$ref": "#/$defs/finding" }, { "type": "null" }], "description": "The draft_changeset findings entry; required once the finding can be drafted. Its id names the file: <id>.json." },
                "argument": { "anyOf": [{ "$ref": "#/$defs/argument" }, { "type": "null" }], "description": "The finding's identity argument, carried unchanged into the changeset." },
                "draftGaps": { "type": ["array", "null"], "items": { "type": "string" }, "description": "What the finding could not place; a non-empty list blocks drafting." },
                "record": { "anyOf": [{ "$ref": "#/$defs/record" }, { "type": "null" }], "description": "The record as match_record input; match_record's findingPath re-matches it." },
                "meta": { "type": ["object", "null"], "description": "The caller's own workflow state. GedFire never reads, validates or rewrites its content." }
              },
              "$defs": {}
            }
            """);
        var defs = body["$defs"]!.AsObject();
        defs["finding"] = Parse(FindingSchemas.FindingJson);
        defs["argument"] = Parse(FindingSchemas.ArgumentJson);
        defs["record"] = Parse(FindingSchemas.RecordJson);
        return body;
    }
}

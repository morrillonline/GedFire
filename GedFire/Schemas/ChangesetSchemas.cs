using System.Text.Json.Nodes;
using GedCore.Contract;
using GedFire.Mcp;

namespace GedFire.Schemas;

// ---------------------------------------------------------------------------
// The JSON Schemas of the changeset contract, built from ChangesetDialect (the
// same table the contract checker reads), and of the files the changeset tools
// read and write: the cards file and the validate/apply and draft reports, built
// from the tools' own output schemas so the two cannot drift apart.
// ---------------------------------------------------------------------------

public static class ChangesetSchemas
{
    // Fields of a tool's response that only its digest carries; a saved report holds the full result.
    static readonly string[] ChangesetDigestFields = ["items", "errorCount", "warningCount", "logCount", "mintedXrefCount", "pointsPreviewTotal"];
    static readonly string[] DraftDigestFields = ["items", "decisionsSummary"];

    /// <summary>The changeset envelope, with every structure and op it holds under $defs.</summary>
    public static JsonObject Changeset()
    {
        var body = ShapeSchema(ChangesetDialect.Shape(ChangesetDialect.Envelope));
        body["properties"]!["schemaVersion"]!["pattern"] = $@"^{ChangesetDialect.SupportedMajor}\.[0-9]+$";
        body["required"] = new JsonArray("schemaVersion", "items");
        body["$defs"] = Definitions();
        return body;
    }

    /// <summary>A cards file: validate_changeset's cardsPath.</summary>
    public static JsonObject Cards() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["cards"] = new JsonObject { ["type"] = "array", ["items"] = JsonNode.Parse(ChangesetToolSchemas.CardJson) },
        },
        ["required"] = new JsonArray("cards"),
    };

    /// <summary>The report validate_changeset and apply_changeset write to reportPath.</summary>
    public static JsonObject ChangesetReport()
    {
        var body = Report(ChangesetToolSchemas.OutputSchemaWithPreviewJson, ChangesetDigestFields);
        body["required"] = new JsonArray("success", "log", "errors", "warnings", "deltas", "mintedXrefs", "copiedSources", "reportWrittenTo");
        return body;
    }

    /// <summary>The report draft_changeset writes to reportPath, its changeset held to the changeset schema.</summary>
    public static JsonObject DraftReport()
    {
        var body = Report(DraftChangesetTool.OutputSchemaJson, DraftDigestFields);
        body["properties"]!["changeset"] = JsonNode.Parse("""
            { "anyOf": [{ "$ref": "#/$defs/changeset" }, { "type": "null" }], "description": "The drafted changeset; null when the findings could not be compiled." }
            """);
        body["required"] = new JsonArray("success", "changeset", "decisions", "errors", "writtenTo", "reportWrittenTo");
        var definitions = Definitions();
        definitions[ChangesetDialect.Envelope] = Changeset().Without("$defs");
        body["$defs"] = definitions;
        return body;
    }

    static JsonObject Report(string outputSchema, string[] digestFields)
    {
        var body = JsonNode.Parse(outputSchema)!.AsObject().Without("$schema");
        body["description"] = "The full result, as written to reportPath.";
        var properties = body["properties"]!.AsObject();
        foreach (string field in digestFields) properties.Remove(field);
        return body;
    }

    static JsonObject Without(this JsonObject node, string key)
    {
        node.Remove(key);
        return node;
    }

    static JsonObject Definitions()
    {
        var definitions = new JsonObject();
        foreach (var shape in ChangesetDialect.Structures.Where(s => s.Name != ChangesetDialect.Envelope))
            definitions[shape.Name] = ShapeSchema(shape);
        foreach (var op in ChangesetDialect.Ops)
            definitions[op.Name] = OpSchema(op);
        return definitions;
    }

    static JsonObject OpSchema(DialectShape op)
    {
        var schema = ShapeSchema(op);
        if (op.Name == "createOrUpdateVital")
        {
            schema["anyOf"] = JsonNode.Parse("""[{ "required": ["value"] }, { "required": ["date"] }, { "required": ["place"] }]""");
            schema["allOf"] = PayloadRules();
        }
        return schema;
    }

    // The tag fixes the payload: an event takes no value, and a text fact no date or place.
    static JsonArray PayloadRules()
    {
        var events = CreateOrUpdateVitalTags(FactPayload.Event);
        var texts = CreateOrUpdateVitalTags(FactPayload.Text);
        return
        [
            new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["fact"] = new JsonObject { ["enum"] = events } } },
                ["then"] = new JsonObject { ["not"] = new JsonObject { ["required"] = new JsonArray("value") } },
            },
            new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["fact"] = new JsonObject { ["enum"] = texts } } },
                ["then"] = JsonNode.Parse("""{ "not": { "anyOf": [{ "required": ["date"] }, { "required": ["place"] }] } }"""),
            },
        ];
    }

    static JsonArray CreateOrUpdateVitalTags(FactPayload payload) =>
        [.. GedCore.Apply.CreateOrUpdateVitalOp.Facts.Where(tag => FactPayloads.Of(tag) == payload).Select(tag => (JsonNode)tag)];

    static JsonObject ShapeSchema(DialectShape shape)
    {
        var properties = new JsonObject();
        foreach (var field in shape.Fields) properties[field.Name] = FieldSchema(field);
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = shape.Description,
            ["properties"] = properties,
        };
        var required = shape.Fields.Where(f => f.Required).Select(f => (JsonNode)f.Name).ToArray();
        if (required.Length > 0) schema["required"] = new JsonArray(required);
        return schema;
    }

    static JsonObject FieldSchema(DialectField field)
    {
        var schema = field.Type switch
        {
            DialectType.String => new JsonObject { ["type"] = "string" },
            DialectType.Integer => new JsonObject { ["type"] = "integer" },
            DialectType.Boolean => new JsonObject { ["type"] = "boolean" },
            DialectType.StringArray => new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            DialectType.Object => Reference(field.Shape!),
            _ => new JsonObject { ["type"] = "array", ["items"] = field.Shape == ChangesetDialect.Op ? AnyOp() : Reference(field.Shape!) },
        };
        schema["description"] = field.Description;
        if (field.Values.Count == 1) schema["const"] = field.Values[0];
        else if (field.Values.Count > 1) schema["enum"] = new JsonArray([.. field.Values.Select(v => (JsonNode)v)]);
        if (field.Pattern is not null) schema["pattern"] = field.Pattern;
        if (field.Minimum is not null) schema["minimum"] = field.Minimum;
        if (field.Maximum is not null) schema["maximum"] = field.Maximum;
        if (field.MinItems is not null) schema["minItems"] = field.MinItems;
        return schema;
    }

    static JsonObject Reference(string shape) => new() { ["$ref"] = $"#/$defs/{shape}" };

    static JsonObject AnyOp() => new()
    {
        ["oneOf"] = new JsonArray([.. ChangesetDialect.Ops.Select(op => (JsonNode)Reference(op.Name))]),
    };
}

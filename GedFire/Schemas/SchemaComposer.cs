using System.Text.Json.Nodes;

namespace GedFire.Schemas;

/// <summary>Builds a JSON Schema from a base document and the parts added to it.</summary>
public static class SchemaComposer
{
    public static string Compose(string baseSchema, Action<JsonNode> addTo)
    {
        var schema = JsonNode.Parse(baseSchema)!;
        addTo(schema);
        return schema.ToJsonString();
    }
}

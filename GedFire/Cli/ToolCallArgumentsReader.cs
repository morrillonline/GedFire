using System.Text.Json;

namespace GedFire.Cli;

public static class ToolCallArgumentsReader
{
    public static async Task<Dictionary<string, JsonElement>> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadObject(document.RootElement);
    }

    public static async Task<Dictionary<string, JsonElement>> ReadAsync(TextReader source, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(await source.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        return ReadObject(document.RootElement);
    }

    static Dictionary<string, JsonElement> ReadObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Tool arguments must be a JSON object, exactly as in an MCP call.");

        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!arguments.TryAdd(property.Name, property.Value.Clone()))
                throw new JsonException($"Duplicate tool argument: {property.Name}");
        return arguments;
    }
}
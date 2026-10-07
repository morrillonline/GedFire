using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Rejects a tools/call whose arguments omit a required parameter or carry an
// unrecognized one, before the SDK's argument binder runs, so the error names
// each offending parameter and lists the accepted ones instead of reducing to
// a generic "error occurred". Reads each tool's own advertised input schema,
// so the check cannot drift from the contract. No aliases and no lenient
// parsing: an unrecognized parameter stays an error.
// ---------------------------------------------------------------------------

public sealed class ToolArgumentGuard
{
    sealed record Parameters(IReadOnlyList<string> Accepted, IReadOnlySet<string> Required);

    readonly Dictionary<string, Parameters> _byTool = new(StringComparer.Ordinal);

    public ToolArgumentGuard(IEnumerable<McpServerTool> tools)
    {
        foreach (var tool in tools)
            _byTool[tool.ProtocolTool.Name] = ReadParameters(tool.ProtocolTool.InputSchema);
    }

    /// <summary>The error naming every missing and unrecognized parameter, or null when the arguments are acceptable.</summary>
    public string? Check(string toolName, IDictionary<string, JsonElement>? arguments)
    {
        if (!_byTool.TryGetValue(toolName, out var parameters)) return null;

        var supplied = arguments ?? new Dictionary<string, JsonElement>();
        var missing = parameters.Accepted
            .Where(p => parameters.Required.Contains(p) &&
                        (!supplied.TryGetValue(p, out var value) || value.ValueKind == JsonValueKind.Null))
            .ToList();
        var unrecognized = supplied.Keys.Where(k => !parameters.Accepted.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count == 0 && unrecognized.Count == 0) return null;

        var problems = new List<string>();
        if (missing.Count > 0) problems.Add(Describe("missing required parameter", missing));
        if (unrecognized.Count > 0) problems.Add(Describe("unrecognized parameter", unrecognized));
        return $"{toolName}: {string.Join("; ", problems)}. {AcceptedText(parameters)}";
    }

    /// <summary>A call-tool filter that answers an invalid call with <see cref="Check"/>'s error and passes any other through.</summary>
    public McpRequestFilter<CallToolRequestParams, CallToolResult> AsFilter() => next => async (context, cancellationToken) =>
    {
        var call = context.Params;
        string? error = call is null ? null : Check(call.Name, call.Arguments);
        return error is null ? await next(context, cancellationToken).ConfigureAwait(false) : CallToolResults.Error(error);
    };

    static string Describe(string what, List<string> names) =>
        $"{what}{(names.Count > 1 ? "s" : "")} {string.Join(", ", names.Select(n => $"\"{n}\""))}";

    static string AcceptedText(Parameters parameters) => parameters.Accepted.Count == 0
        ? "This tool takes no parameters."
        : "Accepted parameters: " + string.Join(", ",
            parameters.Accepted.Select(p => parameters.Required.Contains(p) ? $"{p} (required)" : p)) + ".";

    static Parameters ReadParameters(JsonElement schema)
    {
        var accepted = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(p => p.Name).ToList()
            : [];
        var required = schema.TryGetProperty("required", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(r => r.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
        return new Parameters(accepted, required);
    }
}

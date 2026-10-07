using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The get_records MCP tool: get_record for a list of xrefs in one call. Checks
// the argument shape itself (naming the offending field), obtains the snapshot
// from DocumentSession, and maps each xref through RecordMapper.
// ---------------------------------------------------------------------------

public sealed class GetRecordsTool
{
    public const string ToolName = "get_records";
    public const int MaxXrefs = 500;

    public const string Description =
        "Return several people, families, or sources in one call, each in exactly the shape get_record returns, " +
        "in the same order as the xrefs you pass. Use it instead of repeated get_record calls when you need a " +
        "person together with parents, spouses, and children, or any other known set of records. An xref that " +
        "does not exist comes back as a not_found record in its own slot; it never fails the whole call. Pass " +
        "only xrefs this server has returned. At most 500 xrefs per call. Set includeSources to also receive, " +
        "once each in a top-level sources array, the full text of every source cited by the people and " +
        "families returned. Set fields to receive only recordType, xref, and the named fields of each record " +
        "(for example [\"name\"]), which keeps a large lookup small.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "xrefs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 500,
              "items": { "type": "string", "pattern": "^@[^@]+@$", "minLength": 3 },
              "description": "Local xrefs returned by this server, e.g. \"@I00006@\", \"@F00012@\", or \"@S00042@\". Order is preserved in the result; repeats are allowed."
            },
            "includeSources": {
              "type": "boolean",
              "default": false,
              "description": "When true, add a top-level sources array with the full SourceRecord of every source cited by the people and families returned, each once across the whole call."
            },
            "fields": {
              "type": "array",
              "minItems": 1,
              "uniqueItems": true,
              "items": { "type": "string", "minLength": 1 },
              "description": "Record field names to keep, for example [\"name\", \"birth\"]. Each returned record then holds recordType, xref, and only these fields that its type has. Omit for whole records. A name that no record type has is an error."
            }
          },
          "required": ["xrefs"]
        }
        """;

    public static readonly string OutputSchemaJson = BuildOutputSchema();

    static string BuildOutputSchema()
    {
        var single = JsonNode.Parse(GetRecordTool.OutputSchemaJson)!.AsObject();
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                // anyOf, not oneOf: a record narrowed by fields also fits the last, open alternative.
                ["records"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["anyOf"] = WithNarrowedRecord(single["oneOf"]!.AsArray()) },
                },
                ["sources"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["$ref"] = "#/$defs/SourceRecord" },
                    ["description"] = "Only with includeSources.",
                },
            },
            ["required"] = new JsonArray("records"),
            ["$defs"] = single["$defs"]!.DeepClone(),
        };
        return schema.ToJsonString();
    }

    static JsonArray WithNarrowedRecord(JsonArray fullShapes)
    {
        var alternatives = (JsonArray)fullShapes.DeepClone();
        alternatives.Add(new JsonObject
        {
            ["type"] = "object",
            ["description"] = "A record narrowed by the fields argument.",
            ["properties"] = new JsonObject
            {
                ["recordType"] = new JsonObject { ["type"] = "string" },
                ["xref"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("recordType", "xref"),
        });
        return alternatives;
    }

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly RecordMapper _mapper;

    public GetRecordsTool(DocumentSession session, ToolGate gate, string mediaDir)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _mapper = new RecordMapper(mediaDir);
    }

    public McpServerTool ToMcpServerTool()
    {
        var createOptions = new McpServerToolCreateOptions
        {
            Name = ToolName,
            Description = Description,
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
        };

        var tool = McpServerTool.Create(InvokeAsync, createOptions);
        tool.ProtocolTool.Description = Description;
        tool.ProtocolTool.InputSchema = JsonDocument.Parse(InputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse(OutputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
        };
        return tool;
    }

    // xrefs is bound as a raw JsonElement so a wrong shape reaches ExecuteAsync
    // and is reported by field name instead of failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? xrefs = null, JsonElement? includeSources = null, JsonElement? fields = null,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryReadOptionalBool(includeSources ?? default, "includeSources", out bool include, out string? error))
            return Task.FromResult(CallToolResults.Error(error!));
        return HandleAsync(xrefs ?? default, cancellationToken, include, fields ?? default);
    }

    public async Task<CallToolResult> HandleAsync(
        JsonElement xrefs, CancellationToken cancellationToken, bool includeSources = false, JsonElement fields = default)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(xrefs, includeSources, fields, ct), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CallToolResults.Error($"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    async Task<CallToolResult> ExecuteAsync(
        JsonElement xrefs, bool includeSources, JsonElement fields, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryReadXrefList(xrefs, "xrefs", MaxXrefs, out var list, out string? error))
            return CallToolResults.Error(error!);
        if (!TryReadFields(fields, out var keep, out error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var records = list.Select(xref => _mapper.Map(snapshot.Model, xref)).ToList();
        var sources = includeSources ? _mapper.SourcesCitedBy(snapshot.Model, records) : null;
        IReadOnlyList<object> shown = keep is null ? records : [.. records.Select(r => RecordProjector.Project(r, keep))];
        return CallToolResults.Success(new GetRecordsResult(shown) { Sources = sources }, CallToolResults.JsonOptions);
    }

    static bool TryReadFields(JsonElement fields, out HashSet<string>? keep, out string? error)
    {
        keep = null;
        if (!ToolArguments.TryReadOptionalStringList(fields, "fields", out var names, out error)) return false;
        if (fields.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (names.Count == 0)
        {
            error = "fields must name at least one field, or be omitted to receive whole records.";
            return false;
        }

        for (int i = 0; i < names.Count; i++)
            if (!RecordProjector.AcceptedFields.Contains(names[i]))
            {
                error = $"fields[{i}] \"{names[i]}\" is not a record field. Accepted fields: {string.Join(", ", RecordProjector.AcceptedFields)}.";
                return false;
            }

        keep = [.. names];
        return true;
    }
}

public sealed record GetRecordsResult(IReadOnlyList<object> Records)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SourceRecord>? Sources { get; init; }
}

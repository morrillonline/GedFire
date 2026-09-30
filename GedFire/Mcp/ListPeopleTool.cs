using System.Text.Json;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The list_people MCP tool: every person's xref, primary name, and birth and
// death year, optionally limited to a set of surnames, in stable pages.
// Checks the argument shapes itself, reads the snapshot's MatchIndex, and
// delegates ordering and paging to PersonLister.
// ---------------------------------------------------------------------------

public sealed class ListPeopleTool
{
    public const string ToolName = "list_people";
    public const int DefaultPageSize = 500;
    public const int MaxPageSize = 2000;

    public const string Description =
        "List the people in this server's GEDCOM in a fixed order (surname, given name, xref): each person's " +
        "xref, primary name, surname, and birth and death year. Call this when a task must compare names across " +
        "the whole tree or across every bearer of a surname, which would take thousands of find_person calls. " +
        "Pass \"surnames\" to list only people with those surnames; list every spelling variant you want, since " +
        "matching is exact apart from case and accents. Results come in pages: pass the previous result's " +
        "nextCursor back as \"cursor\" until nextCursor is null. Use find_person to resolve one name, and " +
        "get_record or get_records for detail.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "surnames": {
              "type": "array",
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Only list people with one of these surnames. Include every spelling variant wanted, e.g. [\"Ashworth\", \"Ashwirth\"]. Omit to list everyone."
            },
            "cursor": {
              "type": "string",
              "description": "The nextCursor from the previous page. Omit for the first page."
            },
            "pageSize": {
              "type": "integer",
              "minimum": 1,
              "maximum": 2000,
              "default": 500,
              "description": "People per page, 1 through 2000. Omit for 500."
            }
          }
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "people": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                  "name": { "type": "string" },
                  "surname": { "type": "string" },
                  "birthYear": { "type": ["integer", "null"] },
                  "deathYear": { "type": ["integer", "null"] }
                },
                "required": ["xref", "name", "surname", "birthYear", "deathYear"]
              }
            },
            "totalMatches": { "type": "integer", "minimum": 0, "description": "People matching the surname filter across all pages." },
            "nextCursor": { "type": ["string", "null"], "description": "Pass as cursor for the next page; null on the last page." }
          },
          "required": ["people", "totalMatches", "nextCursor"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;

    public ListPeopleTool(DocumentSession session, ToolGate gate)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
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

    // Arguments arrive as raw JsonElements so a wrong shape is reported by
    // field name in ExecuteAsync rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? surnames = null, JsonElement? cursor = null, JsonElement? pageSize = null,
        CancellationToken cancellationToken = default)
        => HandleAsync(surnames ?? default, cursor ?? default, pageSize ?? default, cancellationToken);

    public async Task<CallToolResult> HandleAsync(
        JsonElement surnames, JsonElement cursor, JsonElement pageSize, CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(surnames, cursor, pageSize, ct), cancellationToken)
                .ConfigureAwait(false);
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
        JsonElement surnames, JsonElement cursor, JsonElement pageSize, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryReadOptionalStringList(surnames, "surnames", out var surnameList, out string? error) ||
            !ToolArguments.TryReadOptionalString(cursor, "cursor", out string? cursorValue, out error) ||
            !ToolArguments.TryReadOptionalInt(pageSize, "pageSize", 1, MaxPageSize, DefaultPageSize, out int size, out error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!PersonLister.TryList(snapshot.MatchIndex, surnameList, cursorValue, size, out var page, out error))
            return CallToolResults.Error(error!);

        return CallToolResults.Success(page, CallToolResults.JsonOptions);
    }
}

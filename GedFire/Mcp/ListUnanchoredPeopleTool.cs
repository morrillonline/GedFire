using System.Text.Json;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The list_unanchored_people MCP tool: people with no dates, no places, and no
// family tie to anyone who has them. Takes no arguments; reads the snapshot's
// model and delegates the analysis to UnanchoredPeopleFinder.
// ---------------------------------------------------------------------------

public sealed class ListUnanchoredPeopleTool
{
    public const string ToolName = "list_unanchored_people";

    public const string Description =
        "List the people in this server's GEDCOM that the tree cannot place: no dated event, no place, and no " +
        "parent, spouse, or child who is placed — either no family ties at all, or ties only to other unplaced " +
        "people. Each entry gives the person, how many facts (events, notes, media, citations) are recorded on " +
        "them, and the unplaced relatives they are tied to. These are either cleanup work or people to merge " +
        "into a placed record; a name like this can also come back from find_person as a perfect match for a " +
        "name alone, so check this list before trusting a bare-name match. People hidden by privacy " +
        "enforcement are never listed. Takes no arguments.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {},
          "required": []
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
                  "factCount": { "type": "integer", "minimum": 0, "description": "Events, notes, media links, and citations recorded on the person." },
                  "tiedOnlyToUnanchored": {
                    "type": "array",
                    "items": { "type": "string", "pattern": "^@[^@]+@$" },
                    "description": "Every parent, spouse, and child of the person, all of them unplaced too. Empty when the person has no family ties."
                  }
                },
                "required": ["xref", "name", "factCount", "tiedOnlyToUnanchored"]
              }
            }
          },
          "required": ["people"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;

    public ListUnanchoredPeopleTool(DocumentSession session, ToolGate gate)
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

    Task<CallToolResult> InvokeAsync(CancellationToken cancellationToken = default) => HandleAsync(cancellationToken);

    public async Task<CallToolResult> HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ExecuteAsync, cancellationToken).ConfigureAwait(false);
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

    async Task<CallToolResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var people = UnanchoredPeopleFinder.Find(snapshot.Model)
            .Select(u => new UnanchoredPersonResult(
                u.Person.Xref, PersonDisplay.FullName(u.Person), u.FactCount, u.TiedOnlyToUnanchored))
            .ToList();
        return CallToolResults.Success(new ListUnanchoredPeopleResult(people), CallToolResults.JsonOptions);
    }
}

public sealed record UnanchoredPersonResult(
    string Xref, string Name, int FactCount, IReadOnlyList<string> TiedOnlyToUnanchored);

public sealed record ListUnanchoredPeopleResult(IReadOnlyList<UnanchoredPersonResult> People);

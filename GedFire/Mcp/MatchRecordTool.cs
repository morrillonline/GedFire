using System.Text.Json;
using GedCore.Matching;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The match_record MCP tool: scores one whole structured historical record
// (a census household, an obituary's survivors, a probate's heirs) against
// the bound document in a single call, replacing the one-find_person-per-name
// survey. Reads the document only; deterministic for the same input.
// ---------------------------------------------------------------------------

public sealed class MatchRecordTool
{
    public const string ToolName = "match_record";

    public const string Description =
        "Score a whole historical record — a census household, an obituary's survivors, a probate's heirs — " +
        "against this server's GEDCOM in one call, instead of one find_person per name. Describe the record's " +
        "persons, the relationships the record itself states among them (by index), and the record's own " +
        "event date and place. For each record person (or just the focus person), tree candidates are found " +
        "by name with find_person's tolerance for spelling and nicknames, then scored on how well each " +
        "candidate's own family subgraph matches the record's: spouse, children, parents, siblings, birth " +
        "year, lifespan against the event date, and place. Returns per person: ranked candidates with a " +
        "composite score and name/lifespan/kinOverlap/place subscores, a closed sameNameSurvey naming every " +
        "same-name person considered with an exclusion reason for each non-leader (died before the event, " +
        "wrong spouse, wrong parents, impossible age, or simply outscored — nobody is silently dropped), and " +
        "a verdict of single (a lone viable candidate, or a composite score of at least 90 with a lead of at least 10), " +
        "candidates, or none. A record " +
        "person matching no tree person comes back with potentialNewPerson true and any near-miss " +
        "possibleDuplicates, never force-matched to the nearest name. Deterministic: the same input always " +
        "returns the same result.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "persons": {
              "type": "array",
              "minItems": 1,
              "maxItems": 50,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "role": { "type": "string", "minLength": 1, "description": "The record's own label for the person, e.g. \"head\", \"wife\", \"son\", \"survivor\", \"deceased\". Free text, echoed back." },
                  "name": { "type": "string", "minLength": 1, "description": "The name as the record spells it." },
                  "sex": { "type": "string", "enum": ["M", "F"] },
                  "birthYear": { "type": "integer", "minimum": 1, "maximum": 9999 },
                  "age": { "type": "integer", "minimum": 0, "maximum": 120, "description": "Age as the record states it; needs event.date with a year, which gives birthYear = event year minus age. Give birthYear or age, not both." },
                  "birthPlace": { "type": "string", "minLength": 1 }
                },
                "required": ["name"]
              },
              "description": "Every person the record names."
            },
            "relationships": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "type": { "type": "string", "enum": ["spouse-of", "child-of", "parent-of", "sibling-of"] },
                  "from": { "type": "integer", "minimum": 0 },
                  "to": { "type": "integer", "minimum": 0 }
                },
                "required": ["type", "from", "to"]
              },
              "description": "Relationships the record itself states, by index into persons: for child-of, from is the child of to; for parent-of, from is a parent of to; spouse-of and sibling-of are symmetric."
            },
            "event": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "date": { "type": "string", "minLength": 1, "description": "GEDCOM-style date of the record's own event, e.g. \"1880\" or \"12 JUN 1880\". Must contain a year." },
                "place": { "type": "string", "minLength": 1 },
                "type": { "type": "string", "minLength": 1, "description": "Free text, e.g. \"census\" or \"obituary\". Context only; never scored." }
              }
            },
            "focus": {
              "type": "integer",
              "minimum": 0,
              "description": "Index of the one person to identify. Omit to identify every person; the others still contribute their names as kin either way."
            }
          },
          "required": ["persons"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "persons": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "index": { "type": "integer", "description": "Index into the request's persons." },
                  "role": { "type": ["string", "null"] },
                  "name": { "type": "string" },
                  "verdict": { "type": "string", "enum": ["single", "candidates", "none"], "description": "single means a lone viable candidate, or a composite score of at least 90 leading the runner-up by at least 10." },
                  "confidentMatchXref": { "type": ["string", "null"] },
                  "confidentMatchScore": { "type": ["number", "null"] },
                  "candidates": {
                    "type": "array",
                    "description": "Viable candidates, best first.",
                    "items": {
                      "type": "object",
                      "properties": {
                        "xref": { "type": "string" },
                        "name": { "type": "string" },
                        "born": { "type": ["string", "null"] },
                        "died": { "type": ["string", "null"] },
                        "score": { "type": "number", "description": "Composite 0-100 over the available subscores." },
                        "subscores": {
                          "type": "object",
                          "properties": {
                            "name": { "type": "number" },
                            "lifespan": { "type": ["number", "null"] },
                            "kinOverlap": { "type": ["number", "null"] },
                            "place": { "type": ["number", "null"] }
                          },
                          "required": ["name", "lifespan", "kinOverlap", "place"]
                        },
                        "matchedKin": {
                          "type": "array",
                          "items": {
                            "type": "object",
                            "properties": {
                              "relation": { "type": "string" },
                              "recordName": { "type": "string" },
                              "treeXref": { "type": "string" },
                              "treeName": { "type": "string" }
                            },
                            "required": ["relation", "recordName", "treeXref", "treeName"]
                          }
                        }
                      },
                      "required": ["xref", "name", "born", "died", "score", "subscores", "matchedKin"]
                    }
                  },
                  "sameNameSurvey": {
                    "type": "array",
                    "description": "Every same-name person considered; each non-leader carries its exclusion reason.",
                    "items": {
                      "type": "object",
                      "properties": {
                        "xref": { "type": "string" },
                        "name": { "type": "string" },
                        "born": { "type": ["string", "null"] },
                        "died": { "type": ["string", "null"] },
                        "outcome": { "type": "string", "enum": ["leading", "candidate", "excluded"] },
                        "reason": { "type": ["string", "null"], "description": "Null only for the leading candidate." }
                      },
                      "required": ["xref", "name", "born", "died", "outcome", "reason"]
                    }
                  },
                  "potentialNewPerson": { "type": "boolean", "description": "True when no tree person remains viable." },
                  "possibleDuplicates": {
                    "type": "array",
                    "description": "Near-miss names a potential new person might duplicate.",
                    "items": {
                      "type": "object",
                      "properties": {
                        "xref": { "type": "string" },
                        "name": { "type": "string" },
                        "reason": { "type": "string" },
                        "score": { "type": "number" }
                      },
                      "required": ["xref", "name", "reason", "score"]
                    }
                  }
                },
                "required": ["index", "role", "name", "verdict", "confidentMatchXref", "confidentMatchScore", "candidates", "sameNameSurvey", "potentialNewPerson", "possibleDuplicates"]
              }
            }
          },
          "required": ["persons"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly RecordMatcher _recordMatcher;

    public MatchRecordTool(DocumentSession session, ToolGate gate, NicknameDirectory nicknames)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        ArgumentNullException.ThrowIfNull(nicknames);
        _recordMatcher = new RecordMatcher(nicknames);
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by
    // field name in MatchRecordRequestReader rather than failing inside the
    // SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? persons = null, JsonElement? relationships = null, JsonElement? @event = null,
        JsonElement? focus = null, CancellationToken cancellationToken = default)
        => HandleAsync(persons ?? default, relationships ?? default, @event ?? default, focus ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement persons, JsonElement relationships, JsonElement recordEvent, JsonElement focus,
        CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(persons, relationships, recordEvent, focus, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(
        JsonElement persons, JsonElement relationships, JsonElement recordEvent, JsonElement focus,
        CancellationToken cancellationToken)
    {
        if (!MatchRecordRequestReader.TryRead(persons, relationships, recordEvent, focus, out var record, out string? error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var result = _recordMatcher.Match(snapshot.MatchIndex, record!);
        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }
}

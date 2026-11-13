using System.Text.Json;
using GedCore.Matching;
using GedFire.Match;
using GedFire.Schemas;
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
        "returns the same result. maxCandidates (default 5) caps candidates per person and candidatesOmitted counts " +
        "the rest; compact returns candidates as {xref, name, lifespan, score, matchedKin} and survey rows as " +
        "{xref, name, reason}. The verdict and runner-up are decided before either applies, and the survey is never capped. " +
        "Instead of giving persons, relationships and event, findingPath reads them from a finding file's record slot; " +
        "the file must be a valid finding file. A record person with an xref is already bound to that tree person: it is " +
        "reported as single on that xref with bound true, its same-name survey still lists the others, and the binding " +
        "anchors the kin scoring of the record's other people. A person the record relates to a bound person is " +
        "resolved to that person's only tree relative of the stated kind whose given name agrees, whatever surname the " +
        "record uses (a wife under her married name), and reported as single with anchoredBy naming the bound person.";

    public static readonly string InputSchemaJson = SchemaComposer.Compose("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "findingPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Path to a finding file whose \"record\" slot holds persons, relationships and event, so a stored record is re-matched without reassembly. Give findingPath or persons, not both; focus, maxCandidates and compact still apply."
            },
            "focus": {
              "type": "integer",
              "minimum": 0,
              "description": "Index of the one person to identify. Omit to identify every person; the others still contribute their names as kin either way."
            },
            "maxCandidates": {
              "type": "integer",
              "minimum": 1,
              "maximum": 20,
              "description": "Most candidates returned per person, default 5. The verdict, confident match and runner-up are decided first."
            },
            "compact": {
              "type": "boolean",
              "description": "Return candidates as {xref, name, lifespan, score, matchedKin} without subscores, and sameNameSurvey rows as {xref, name, reason}."
            }
          }
        }
        """, FindingSchemas.AddRecordProperties);

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
                  "verdict": { "type": "string", "enum": ["single", "candidates", "none"], "description": "single means a lone viable candidate, or a composite score of at least 90 leading the runner-up by at least 10, or a person the record binds by xref, or a bound person's only matching tree relative of the stated kind." },
                  "bound": { "type": "boolean", "description": "True when the record bound this person by xref; confidentMatchXref is then that xref." },
                  "anchoredBy": { "type": ["string", "null"], "description": "The xref of the bound record person whose only matching tree relative of the stated kind (given name agreeing, surname not compared) this person was resolved to; null otherwise." },
                  "confidentMatchXref": { "type": ["string", "null"] },
                  "confidentMatchScore": { "type": ["number", "null"] },
                  "runnerUpXref": { "type": ["string", "null"], "description": "The second-ranked candidate, kept even when maxCandidates drops it." },
                  "runnerUpScore": { "type": ["number", "null"] },
                  "candidatesOmitted": { "type": "integer", "minimum": 0, "description": "Viable candidates left out by maxCandidates." },
                  "candidates": {
                    "type": "array",
                    "description": "Viable candidates, best first, at most maxCandidates. Compact rows carry lifespan instead of born, died and subscores.",
                    "items": {
                      "type": "object",
                      "properties": {
                        "xref": { "type": "string" },
                        "name": { "type": "string" },
                        "born": { "type": ["string", "null"] },
                        "died": { "type": ["string", "null"] },
                        "lifespan": { "type": ["string", "null"], "description": "Compact only: birth-death years, ? for an unknown end." },
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
                      "required": ["xref", "name", "score", "matchedKin"]
                    }
                  },
                  "sameNameSurvey": {
                    "type": "array",
                    "description": "Every same-name person considered; each non-leader carries its exclusion reason. Never capped; compact rows are {xref, name, reason}.",
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
                      "required": ["xref", "name", "reason"]
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
                "required": ["index", "role", "name", "verdict", "confidentMatchXref", "confidentMatchScore", "runnerUpXref", "runnerUpScore", "candidates", "candidatesOmitted", "sameNameSurvey", "potentialNewPerson", "possibleDuplicates"]
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
        JsonElement? focus = null, JsonElement? maxCandidates = null, JsonElement? compact = null,
        JsonElement? findingPath = null, CancellationToken cancellationToken = default)
        => HandleAsync(
            new MatchRecordArguments(persons ?? default, relationships ?? default, @event ?? default, focus ?? default)
            {
                MaxCandidates = maxCandidates ?? default, Compact = compact ?? default, FindingPath = findingPath ?? default,
            },
            cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement persons, JsonElement relationships, JsonElement recordEvent, JsonElement focus,
        CancellationToken cancellationToken) =>
        HandleAsync(new MatchRecordArguments(persons, relationships, recordEvent, focus), cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement persons, JsonElement relationships, JsonElement recordEvent, JsonElement focus,
        JsonElement maxCandidates, JsonElement compact, CancellationToken cancellationToken) =>
        HandleAsync(
            new MatchRecordArguments(persons, relationships, recordEvent, focus) { MaxCandidates = maxCandidates, Compact = compact },
            cancellationToken);

    public Task<CallToolResult> HandleAsync(MatchRecordArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(arguments, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(MatchRecordArguments arguments, CancellationToken cancellationToken)
    {
        if (!TryResolveRecord(arguments, out var described, out string? error))
            return CallToolResults.Error(error!);
        if (!MatchRecordRequestReader.TryRead(described.Persons, described.Relationships, described.Event, arguments.Focus, out var record, out error))
            return CallToolResults.Error(error!);
        if (!MatchRecordRequestReader.TryReadView(arguments.MaxCandidates, arguments.Compact, out var view, out error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (UnknownBinding(record!, snapshot) is { } unknown) return CallToolResults.Error(unknown);
        var result = _recordMatcher.Match(snapshot.MatchIndex, record!);
        return CallToolResults.Success(view!.Present(result), CallToolResults.JsonOptions);
    }

    // The record is given inline, or read from the record slot of a finding file.
    static bool TryResolveRecord(MatchRecordArguments arguments, out MatchRecordArguments described, out string? error)
    {
        described = arguments;
        if (!ToolArguments.TryReadOptionalString(arguments.FindingPath, "findingPath", out string? path, out error)) return false;
        if (path is null) return true;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "findingPath must not be blank.";
            return false;
        }
        if (!IsAbsent(arguments.Persons) || !IsAbsent(arguments.Relationships) || !IsAbsent(arguments.Event))
        {
            error = "give findingPath, or persons, relationships and event, not both.";
            return false;
        }

        var file = FindingFileReader.ReadPath(Path.GetFullPath(path.Trim()));
        if (file.Errors.Count > 0)
        {
            error = string.Join(" ", file.Errors);
            return false;
        }
        if (file.Record is not { } slot)
        {
            error = $"{file.Name} has no record.";
            return false;
        }
        described = arguments with { Persons = Member(slot, "persons"), Relationships = Member(slot, "relationships"), Event = Member(slot, "event") };
        return true;
    }

    static string? UnknownBinding(RecordDescription record, DocumentSnapshot snapshot) =>
        record.Persons.Select((person, index) => (person.Xref, index))
            .Where(p => p.Xref is not null && !snapshot.Model.Individuals.ContainsKey(p.Xref))
            .Select(p => $"persons[{p.index}].xref {p.Xref} is not a person in the document.")
            .FirstOrDefault();

    static JsonElement Member(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : default;

    static bool IsAbsent(JsonElement element) => element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
}

/// <summary>match_record's raw arguments: the record inline (persons, relationships, event), or a finding file holding it.</summary>
public sealed record MatchRecordArguments(JsonElement Persons, JsonElement Relationships, JsonElement Event, JsonElement Focus)
{
    public JsonElement MaxCandidates { get; init; }
    public JsonElement Compact { get; init; }
    public JsonElement FindingPath { get; init; }
}

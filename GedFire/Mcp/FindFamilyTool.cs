using System.Text.Json;
using GedCore;
using GedCore.Matching;
using GedFire.Gen;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The find_family MCP tool: find people through a relative whose name is the
// only thing known ("the widow of Joseph", "the mother named Jane"). Checks the
// argument shapes itself, reads the snapshot's MatchIndex, and delegates the
// search to FamilyMatcher.
// ---------------------------------------------------------------------------

public sealed class FindFamilyTool
{
    public const string ToolName = "find_family";
    public const int DefaultMaxResults = 8;
    public const int MaximumMaxResults = 20;

    public const string Description =
        "Find people in this server's GEDCOM through a relative whose name is all you know, when find_person " +
        "cannot start from the person's own name. Say how the name you know is related to the person you want: " +
        "relation \"spouse\" finds people married to that name (\"the widow of Joseph Ashworth\"); \"child\" finds " +
        "the parents of someone with that name (\"the mother of Levi Ashworth\"); \"parent\" finds the children " +
        "of someone with that name (\"a child of Jane Whitcombe\"). Each result gives the person, the family xref that " +
        "connects them, and the relative that matched, best first. Optional hints (birth and death year or place) " +
        "describe the person you are looking for and only rank people the relative's name already found. Pass a " +
        "person's xref to get_record or get_records for detail.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "relation": {
              "enum": ["spouse", "child", "parent"],
              "description": "How the name you know is related to the person you want: their spouse, their child, or their parent."
            },
            "name": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "The relative's name as the user said it: a full name, given name, shortened prefix, documented nickname, or close spelling. Pass it unchanged; the tool normalizes it."
            },
            "hints": {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 1,
              "description": "Facts about the person you are looking for (not the relative), used only to rank. Only birth and death are accepted.",
              "properties": {
                "birth": {
                  "type": "object",
                  "additionalProperties": false,
                  "minProperties": 1,
                  "properties": {
                    "year": { "type": "integer", "minimum": 1, "maximum": 9999, "description": "An exact or approximate birth year." },
                    "place": { "type": "string", "minLength": 1, "pattern": "\\S", "description": "A birth place as free text." }
                  }
                },
                "death": {
                  "type": "object",
                  "additionalProperties": false,
                  "minProperties": 1,
                  "properties": {
                    "year": { "type": "integer", "minimum": 1, "maximum": 9999, "description": "An exact or approximate death year." },
                    "place": { "type": "string", "minLength": 1, "pattern": "\\S", "description": "A death place as free text." }
                  }
                }
              }
            },
            "maxResults": {
              "type": "integer",
              "minimum": 1,
              "maximum": 20,
              "default": 8,
              "description": "The most results to return, 1 through 20. Omit for 8."
            }
          },
          "required": ["relation", "name"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "candidates": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "person": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                      "name": { "type": "string" },
                      "birthYear": { "type": ["integer", "null"] },
                      "deathYear": { "type": ["integer", "null"] }
                    },
                    "required": ["xref", "name", "birthYear", "deathYear"]
                  },
                  "familyXref": { "type": "string", "pattern": "^@[^@]+@$", "description": "The family that connects the person to the matched relative." },
                  "relative": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                      "name": { "type": "string" },
                      "nameScore": { "type": "number", "description": "How well the relative's name matched, 0 to 100." }
                    },
                    "required": ["xref", "name", "nameScore"]
                  },
                  "matchScore": { "type": "number", "description": "The ranking score: the relative's name score, averaged with how well the person fits the hints when hints are given." }
                },
                "required": ["person", "familyXref", "relative", "matchScore"]
              }
            },
            "totalMatches": { "type": "integer", "minimum": 0 },
            "truncated": { "type": "boolean", "description": "True when maxResults cut off further results." }
          },
          "required": ["candidates", "totalMatches", "truncated"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly FamilyMatcher _matcher;

    public FindFamilyTool(DocumentSession session, ToolGate gate, NicknameDirectory nicknames)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _matcher = new FamilyMatcher(nicknames ?? throw new ArgumentNullException(nameof(nicknames)));
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
        JsonElement? relation = null, JsonElement? name = null, JsonElement? hints = null,
        JsonElement? maxResults = null, CancellationToken cancellationToken = default)
        => HandleAsync(relation ?? default, name ?? default, hints ?? default, maxResults ?? default, cancellationToken);

    public async Task<CallToolResult> HandleAsync(
        JsonElement relation, JsonElement name, JsonElement hints, JsonElement maxResults,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(relation, name, hints, maxResults, ct), cancellationToken)
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
        JsonElement relation, JsonElement name, JsonElement hints, JsonElement maxResults,
        CancellationToken cancellationToken)
    {
        if (!TryReadRelation(relation, out var parsedRelation, out string? error) ||
            !TryReadName(name, out string? nameText, out error) ||
            !TryReadHints(hints, out var matchHints, out error) ||
            !ToolArguments.TryReadOptionalInt(
                maxResults, "maxResults", 1, MaximumMaxResults, DefaultMaxResults, out int max, out error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var outcome = _matcher.Find(snapshot.MatchIndex, parsedRelation, nameText!, matchHints, max);
        return CallToolResults.Success(Map(outcome), CallToolResults.JsonOptions);
    }

    static bool TryReadRelation(JsonElement element, out FamilyRelation relation, out string? error)
    {
        relation = default;
        const string accepted = "\"spouse\", \"child\", or \"parent\"";
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            error = $"relation is required: {accepted}.";
            return false;
        }
        relation = element.ValueKind == JsonValueKind.String ? element.GetString() switch
        {
            "spouse" => FamilyRelation.Spouse,
            "child" => FamilyRelation.Child,
            "parent" => FamilyRelation.Parent,
            _ => (FamilyRelation)(-1),
        } : (FamilyRelation)(-1);
        if (!Enum.IsDefined(relation))
        {
            error = $"relation must be {accepted}, not {Describe(element)}.";
            return false;
        }
        error = null;
        return true;
    }

    static bool TryReadName(JsonElement element, out string? name, out string? error)
    {
        name = null;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            error = "name is required: the relative's name as a non-blank string.";
            return false;
        }
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = $"name must be a non-blank string, not {(element.ValueKind == JsonValueKind.String ? "a blank string" : ToolArguments.Describe(element))}.";
            return false;
        }
        name = element.GetString()!.Trim();
        error = null;
        return true;
    }

    static bool TryReadHints(JsonElement element, out MatchHints? hints, out string? error)
    {
        hints = null;
        if (!FindPersonHintsReader.TryRead(element, out var args, out error)) return false;
        if (args is null) return true;
        if (!FindPersonTool.TryValidateHints(args, out error)) return false;
        if (args.Parents is not null || args.Spouse is not null)
        {
            error = $"hints.{(args.Parents is not null ? "parents" : "spouse")} is not accepted by find_family: " +
                    "hints describe the person you are looking for and accept only birth and death.";
            return false;
        }
        hints = new MatchHints(ToEvent(args.Birth), ToEvent(args.Death));
        return true;
    }

    static EventHint? ToEvent(FindPersonEventHintArgs? hint) =>
        hint is null ? null : new EventHint(hint.Year, hint.Place);

    static string Describe(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? $"\"{element.GetString()}\""
        : ToolArguments.Describe(element);

    static FindFamilyResult Map(FamilySearchOutcome outcome) => new(
        [.. outcome.Candidates.Select(c => new FamilyCandidateResult(
            new FamilyPersonResult(c.Person.Xref, PersonDisplay.FullName(c.Person), YearOf(c.Person.Birth), YearOf(c.Person.Death)),
            c.Family.Xref,
            new FamilyRelativeResult(c.Relative.Person.Xref, PersonDisplay.FullName(c.Relative.Person), c.Relative.MatchScore),
            c.RankScore))],
        outcome.TotalMatches,
        outcome.Truncated);

    static int? YearOf(GedEvent? ev)
    {
        int year = GedDate.ParseYear(ev?.Date);
        return year != 0 ? year : null;
    }
}

public sealed record FamilyPersonResult(string Xref, string Name, int? BirthYear, int? DeathYear);

public sealed record FamilyRelativeResult(string Xref, string Name, double NameScore);

public sealed record FamilyCandidateResult(
    FamilyPersonResult Person, string FamilyXref, FamilyRelativeResult Relative, double MatchScore);

public sealed record FindFamilyResult(IReadOnlyList<FamilyCandidateResult> Candidates, int TotalMatches, bool Truncated);

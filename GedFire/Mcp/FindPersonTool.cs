using System.Text.Json;
using System.Text.Json.Serialization;
using GedFire.Gen;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The find_person MCP tool (docs/design/mcp-server.md "The first tool:
// find_person"). Declares the tool's metadata, schemas, and annotations
// verbatim from that document; trims and validates the query; obtains the
// snapshot from DocumentSession; calls PersonMatcher; maps the MatchOutcome
// to the result records. The last-chance Exception handler lives here. No
// matching or scoring logic lives in this class.
// ---------------------------------------------------------------------------

public sealed class FindPersonTool
{
    public const string ToolName = "find_person";

    public const string Description =
        "Resolve a name the user mentioned to a person in this server's GEDCOM. Call this whenever a person is " +
        "known by name but not by xref. A single result includes the person's identity, their child-family xref, " +
        "and the xref of every marriage — childless marriages included — with marriage date and spouse name; " +
        "pass those xrefs to future family detail or research tools when needed. When candidates are returned, " +
        "ask the user which person they mean and call again with any new birth, place, spouse, or parent hint.";

    // Verbatim from docs/design/mcp-server.md "Input".
    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": {
              "type": "string",
              "minLength": 1,
              "description": "The name as the user said it: a full name, given name, shortened prefix such as Fred for Frederick, a documented nickname such as Bill for William, or a close spelling. Pass it unchanged; the tool normalizes it."
            },
            "hints": {
              "type": "object",
              "additionalProperties": false,
              "description": "Any incidental detail the user mentioned in the same breath, used only to rank and narrow candidates. All fields optional; omit any not mentioned.",
              "properties": {
                "birthYear": {
                  "type": "integer",
                  "minimum": 1,
                  "maximum": 9999,
                  "description": "An approximate or exact birth year, if mentioned."
                },
                "place": {
                  "type": "string",
                  "minLength": 1,
                  "description": "A place associated with the person (birth, death, residence), as free text."
                },
                "spouseName": {
                  "type": "string",
                  "minLength": 1,
                  "description": "A spouse's name, if the user mentioned one."
                },
                "parentName": {
                  "type": "string",
                  "minLength": 1,
                  "description": "A parent's name, if the user mentioned one."
                }
              }
            }
          },
          "required": ["query"]
        }
        """;

    // Verbatim from docs/design/mcp-server.md "Output: three shapes, one schema".
    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "oneOf": [
            { "$ref": "#/$defs/SingleMatch" },
            { "$ref": "#/$defs/CandidateList" },
            { "$ref": "#/$defs/NoMatch" }
          ],
          "$defs": {
            "EventIdentity": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "properties": {
                "date": { "type": ["string", "null"] },
                "year": { "type": ["integer", "null"] },
                "qualifier": { "type": ["string", "null"] },
                "place": { "type": ["string", "null"] }
              },
              "required": ["date", "year", "qualifier", "place"]
            },
            "ParentsIdentity": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "properties": {
                "father": { "type": ["string", "null"] },
                "mother": { "type": ["string", "null"] }
              },
              "required": ["father", "mother"]
            },
            "CandidateIdentity": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "name": { "type": "string" },
                "birth": { "$ref": "#/$defs/EventIdentity" },
                "death": { "$ref": "#/$defs/EventIdentity" },
                "parents": { "$ref": "#/$defs/ParentsIdentity" },
                "spouses": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["xref", "name", "birth", "death", "parents", "spouses"]
            },
            "SpouseFamilyIdentity": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "marriageDate": { "type": ["string", "null"] },
                "spouseName": { "type": ["string", "null"] }
              },
              "required": ["xref", "marriageDate", "spouseName"]
            },
            "FamiliesIdentity": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "asChild": {
                  "type": "array",
                  "items": { "type": "string", "pattern": "^@[^@]+@$" }
                },
                "asParent": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/SpouseFamilyIdentity" }
                }
              },
              "required": ["asChild", "asParent"]
            },
            "ResolvedPersonIdentity": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "name": { "type": "string" },
                "birth": { "$ref": "#/$defs/EventIdentity" },
                "death": { "$ref": "#/$defs/EventIdentity" },
                "families": { "$ref": "#/$defs/FamiliesIdentity" }
              },
              "required": ["xref", "name", "birth", "death", "families"]
            },
            "Suggestion": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "name": { "type": "string" },
                "reason": {
                  "type": "string",
                  "enum": ["close spelling", "partial name"]
                }
              },
              "required": ["xref", "name", "reason"]
            },
            "SingleMatch": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "matchType": { "const": "single" },
                "person": { "$ref": "#/$defs/ResolvedPersonIdentity" }
              },
              "required": ["matchType", "person"]
            },
            "CandidateList": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "matchType": { "const": "candidates" },
                "candidates": {
                  "type": "array",
                  "minItems": 2,
                  "maxItems": 8,
                  "items": { "$ref": "#/$defs/CandidateIdentity" }
                },
                "truncated": { "type": "boolean" }
              },
              "required": ["matchType", "candidates", "truncated"]
            },
            "NoMatch": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "matchType": { "const": "none" },
                "suggestions": {
                  "type": "array",
                  "maxItems": 3,
                  "items": { "$ref": "#/$defs/Suggestion" }
                }
              },
              "required": ["matchType", "suggestions"]
            }
          }
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly NicknameDirectory _nicknames;

    public FindPersonTool(DocumentSession session, ToolGate gate, NicknameDirectory nicknames)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _nicknames = nicknames ?? throw new ArgumentNullException(nameof(nicknames));
    }

    /// <summary>
    /// Build the SDK's McpServerTool for this instance: wire the delegate for
    /// argument binding and invocation, then overwrite the advertised
    /// description/schemas/annotations with this class's hand-written,
    /// doc-verbatim constants so they are the contract, not a byproduct of
    /// reflection over the delegate's parameter types.
    /// </summary>
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

    // The delegate McpServerTool.Create binds arguments to and invokes.
    // "hints" needs a real default so the SDK's reflection-based argument
    // binder treats it as optional rather than throwing when a client omits
    // it entirely (as most calls will).
    Task<CallToolResult> InvokeAsync(string query, FindPersonHintsArgs? hints = null, CancellationToken cancellationToken = default)
        => HandleAsync(query, hints, cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure — including a rate-limit rejection from
    /// ToolGate, which is thrown before the work below ever starts — becomes
    /// an isError CallToolResult (docs/design/mcp-server.md "Errors"), except
    /// that an already-requested cancellation is left to propagate as
    /// OperationCanceledException so no late response is emitted.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(
        string query, FindPersonHintsArgs? hints, CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(query, hints, ct), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Last-chance handler: the stack trace is deliberately included —
            // this is a local tool run by the researcher against their own
            // data (docs/design/mcp-server.md "Errors").
            return CallToolResults.Error($"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    async Task<CallToolResult> ExecuteAsync(string query, FindPersonHintsArgs? hints, CancellationToken cancellationToken)
    {
        string trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0)
            return CallToolResults.Error("query must not be blank.");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var matcher = new PersonMatcher(_nicknames);
        // trimmed.Length > 0 here guarantees query is non-null; the matcher
        // still receives the original, untrimmed query (it normalizes on its
        // own terms) rather than the trimmed copy.
        var outcome = matcher.Match(snapshot.MatchIndex, query!, ToMatchHints(hints));

        return CallToolResults.Success(MapOutcome(outcome), CallToolResults.JsonOptions);
    }

    static MatchHints ToMatchHints(FindPersonHintsArgs? hints) =>
        hints is null ? MatchHints.None : new MatchHints(hints.BirthYear, hints.Place, hints.SpouseName, hints.ParentName);

    // -------------------------------------------------------------------
    // MatchOutcome -> result record mapping
    // -------------------------------------------------------------------

    static object MapOutcome(MatchOutcome outcome) => outcome.PersonMatchType switch
    {
        PersonMatchType.Single => new SingleMatchResult("single", MapResolvedPerson(outcome.Matches[0].Individual)),
        PersonMatchType.Candidates => new CandidateListResult(
            "candidates",
            [.. outcome.Matches.Select(m => MapCandidate(m.Individual))],
            outcome.Truncated),
        PersonMatchType.None => new NoMatchResult("none", [.. outcome.Suggestions.Select(MapSuggestion)]),
        _ => throw new InvalidOperationException($"Unknown match type: {outcome.PersonMatchType}"),
    };

    static ResolvedPersonIdentity MapResolvedPerson(GedIndividual indi) => new(
        indi.Xref,
        PersonDisplay.FullName(indi),
        MapEvent(indi.Birth),
        MapEvent(indi.Death),
        new FamiliesIdentity(MapAsChild(indi), MapAsParent(indi)));

    static CandidateIdentity MapCandidate(GedIndividual indi) => new(
        indi.Xref,
        PersonDisplay.FullName(indi),
        MapEvent(indi.Birth),
        MapEvent(indi.Death),
        MapParents(indi.FamChild),
        MapSpouseNames(indi));

    static SuggestionIdentity MapSuggestion(Suggestion s) => new(
        s.Individual.Xref,
        PersonDisplay.FullName(s.Individual),
        s.Reason == SuggestionReason.CloseSpelling ? "close spelling" : "partial name");

    static EventIdentity? MapEvent(GedEvent? ev)
    {
        if (ev is null) return null;
        string? date = ev.Date.Length > 0 ? ev.Date : null;
        string? place = ev.Place.Length > 0 ? ev.Place : null;
        if (date is null && place is null) return null;

        int year = GedDate.ParseYear(ev.Date);
        return new EventIdentity(date, year != 0 ? year : null, GedDate.Qualifier(ev.Date), place);
    }

    static ParentsIdentity? MapParents(GedFamily? famChild)
    {
        if (famChild is null) return null;
        return new ParentsIdentity(
            famChild.Husband != null ? PersonDisplay.FullName(famChild.Husband) : null,
            famChild.Wife != null ? PersonDisplay.FullName(famChild.Wife) : null);
    }

    static List<string> MapSpouseNames(GedIndividual indi) =>
        [.. indi.FamSpouse
            .Select(f => f.SpouseOf(indi))
            .Where(spouse => spouse != null)
            .Select(spouse => PersonDisplay.FullName(spouse!))];

    static List<string> MapAsChild(GedIndividual indi) =>
        indi.FamChild != null ? [indi.FamChild.Xref] : [];

    static List<SpouseFamilyIdentity> MapAsParent(GedIndividual indi) =>
        [.. indi.FamSpouse.Select(f => new SpouseFamilyIdentity(
            f.Xref,
            f.Marriage != null && f.Marriage.Date.Length > 0 ? f.Marriage.Date : null,
            f.SpouseOf(indi) is { } spouse ? PersonDisplay.FullName(spouse) : null))];
}

/// <summary>
/// Wire shape of the "hints" argument object. Binding-only concern — the
/// domain type PersonMatcher actually consumes is GedFire.Match.MatchHints.
/// </summary>
public sealed class FindPersonHintsArgs
{
    [JsonPropertyName("birthYear")]
    public int? BirthYear { get; init; }

    [JsonPropertyName("place")]
    public string? Place { get; init; }

    [JsonPropertyName("spouseName")]
    public string? SpouseName { get; init; }

    [JsonPropertyName("parentName")]
    public string? ParentName { get; init; }
}

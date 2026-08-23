using System.Text.Json;
using System.Text.Json.Serialization;
using GedCore;
using GedCore.Matching;
using GedFire.Gen;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The find_person MCP tool. Declares the tool's metadata, schemas, and
// annotations; trims and validates the query; obtains the snapshot from
// DocumentSession; calls PersonMatcher; maps the MatchOutcome to the result
// records. The last-chance Exception handler lives here. No matching or
// scoring logic lives in this class.
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
            },
            "maxResults": {
              "oneOf": [
                { "type": "integer", "minimum": 1 },
                { "type": "string", "const": "all" }
              ],
              "default": 8,
              "description": "The most scored recall candidates to return. An integer caps the list at that size; \"all\" returns the complete scored recall set. This never changes the matcher's confidence classification or totalMatches. Use \"all\" to correlate a finding by checking the selected person against every plausible same-name alternative. Omit for the default of 8."
            }
          },
          "required": ["query"]
        }
        """;

    // One scored shape and a true recall count for every response, no oneOf.
    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "matchType": { "type": "string", "enum": ["none", "single", "candidates"] },
            "confidentMatchXref": { "type": ["string", "null"], "pattern": "^@[^@]+@$" },
            "confidentMatchScore": { "type": ["number", "null"] },
            "person": { "$ref": "#/$defs/ResolvedPersonIdentity" },
            "candidates": {
              "type": "array",
              "items": { "$ref": "#/$defs/CandidateIdentity" }
            },
            "suggestions": {
              "type": "array",
              "maxItems": 3,
              "items": { "$ref": "#/$defs/Suggestion" }
            },
            "totalMatches": { "type": "integer", "minimum": 0 },
            "truncated": { "type": "boolean" }
          },
          "required": [
            "matchType", "confidentMatchXref", "confidentMatchScore", "person",
            "candidates", "suggestions", "totalMatches", "truncated"
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
                "spouses": { "type": "array", "items": { "type": "string" } },
                "matchScore": { "type": "number" }
              },
              "required": ["xref", "name", "birth", "death", "parents", "spouses", "matchScore"]
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
              "type": ["object", "null"],
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
                },
                "matchScore": { "type": "number" }
              },
              "required": ["xref", "name", "reason", "matchScore"]
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
    // it entirely (as most calls will). "maxResults" is bound as a raw
    // JsonElement since its wire shape is an integer-or-"all" union with no
    // single natural CLR type.
    Task<CallToolResult> InvokeAsync(
        string query, FindPersonHintsArgs? hints = null, JsonElement? maxResults = null, CancellationToken cancellationToken = default)
        => HandleAsync(query, hints, cancellationToken, maxResults);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure — including a rate-limit rejection from
    /// ToolGate, which is thrown before the work below ever starts — becomes
    /// an isError CallToolResult, except that an already-requested
    /// cancellation is left to propagate as
    /// OperationCanceledException so no late response is emitted.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(
        string query, FindPersonHintsArgs? hints, CancellationToken cancellationToken, JsonElement? maxResults = null)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(query, hints, maxResults, ct), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Last-chance handler: the stack trace is deliberately included —
            // this is a local tool run by the researcher against their own
            // data.
            return CallToolResults.Error($"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    async Task<CallToolResult> ExecuteAsync(
        string query, FindPersonHintsArgs? hints, JsonElement? maxResults, CancellationToken cancellationToken)
    {
        string trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0)
            return CallToolResults.Error("query must not be blank.");

        if (!TryParseMaxResults(maxResults, out int? cap, out string? capError))
            return CallToolResults.Error(capError!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var matcher = new PersonMatcher(_nicknames);
        // trimmed.Length > 0 here guarantees query is non-null; the matcher
        // still receives the original, untrimmed query (it normalizes on its
        // own terms) rather than the trimmed copy.
        var outcome = matcher.Match(snapshot.MatchIndex, query!, ToMatchHints(hints), cap);

        return CallToolResults.Success(MapOutcome(outcome), CallToolResults.JsonOptions);
    }

    static MatchHints ToMatchHints(FindPersonHintsArgs? hints) =>
        hints is null ? MatchHints.None : new MatchHints(hints.BirthYear, hints.Place, hints.SpouseName, hints.ParentName);

    // Default cap when the caller omits "maxResults" entirely (JSON schema
    // "default": 8). null means "all" -- no cap at all.
    const int DefaultMaxResults = 8;

    /// <summary>
    /// Parse the wire "maxResults" argument: absent -> the default cap of 8;
    /// the string "all" -> no cap (null); a positive integer -> that cap.
    /// Anything else is a validation error: zero and negative values are
    /// malformed input.
    /// </summary>
    static bool TryParseMaxResults(JsonElement? raw, out int? cap, out string? error)
    {
        if (raw is null || raw.Value.ValueKind == JsonValueKind.Undefined || raw.Value.ValueKind == JsonValueKind.Null)
        {
            cap = DefaultMaxResults;
            error = null;
            return true;
        }

        var value = raw.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            if (value.GetString() == "all")
            {
                cap = null;
                error = null;
                return true;
            }
            cap = null;
            error = $"maxResults must be a positive integer or \"all\", got: \"{value.GetString()}\".";
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) && n >= 1)
        {
            cap = n;
            error = null;
            return true;
        }

        cap = null;
        error = "maxResults must be a positive integer or \"all\".";
        return false;
    }

    // -------------------------------------------------------------------
    // MatchOutcome -> result record mapping
    // -------------------------------------------------------------------

    static FindPersonResult MapOutcome(MatchOutcome outcome)
    {
        bool isSingle = outcome.PersonMatchType == PersonMatchType.Single;
        string matchType = outcome.PersonMatchType switch
        {
            PersonMatchType.Single => "single",
            PersonMatchType.Candidates => "candidates",
            PersonMatchType.None => "none",
            _ => throw new InvalidOperationException($"Unknown match type: {outcome.PersonMatchType}"),
        };

        return new FindPersonResult(
            matchType,
            isSingle ? outcome.Matches[0].Individual.Xref : null,
            isSingle ? outcome.Matches[0].FinalScore : null,
            isSingle ? MapResolvedPerson(outcome.Matches[0].Individual) : null,
            [.. outcome.Matches.Select(MapCandidate)],
            [.. outcome.Suggestions.Select(MapSuggestion)],
            outcome.TotalMatches,
            outcome.Truncated);
    }

    static ResolvedPersonIdentity MapResolvedPerson(GedIndividual indi) => new(
        indi.Xref,
        PersonDisplay.FullName(indi),
        MapEvent(indi.Birth),
        MapEvent(indi.Death),
        new FamiliesIdentity(MapAsChild(indi), MapAsParent(indi)));

    static CandidateIdentity MapCandidate(ScoredMatch match)
    {
        var indi = match.Individual;
        return new(
            indi.Xref,
            PersonDisplay.FullName(indi),
            MapEvent(indi.Birth),
            MapEvent(indi.Death),
            MapParents(indi.FamChild),
            MapSpouseNames(indi),
            match.FinalScore);
    }

    static SuggestionIdentity MapSuggestion(Suggestion s) => new(
        s.Individual.Xref,
        PersonDisplay.FullName(s.Individual),
        s.Reason == SuggestionReason.CloseSpelling ? "close spelling" : "partial name",
        s.Score);

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

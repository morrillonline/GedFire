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
      "ask the user which person they mean and call again with any new birth or death year/place, father or " +
      "mother name, spouse name, or marriage year/place. Hints rank only people already recalled by name.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "The name as the user said it: a full name, given name, shortened prefix such as Fred for Frederick, a documented nickname such as Bill for William, or a close spelling. Pass it unchanged; the tool normalizes it."
            },
            "hints": {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 1,
              "description": "Structured facts the user mentioned, used only to rank and narrow people already recalled by query. Omit unknown facts and omit hints entirely when none are known. Empty objects, blank strings, legacy flat properties, and unknown properties are invalid. Missing candidate data is not penalized.",
              "properties": {
                "birth": {
                  "type": "object",
                  "additionalProperties": false,
                  "minProperties": 1,
                  "description": "Birth evidence. Its year and place compare only with the candidate's birth event, never death, residence, or census events.",
                  "properties": {
                    "year": {
                      "type": "integer",
                      "minimum": 1,
                      "maximum": 9999,
                      "description": "An exact or approximate birth year. Exact matches score highest; one- and two-year differences receive partial credit."
                    },
                    "place": {
                      "type": "string",
                      "minLength": 1,
                      "pattern": "\\S",
                      "description": "A birth place as free text. It compares only with the recorded birth place using normalized containment."
                    }
                  }
                },
                "death": {
                  "type": "object",
                  "additionalProperties": false,
                  "minProperties": 1,
                  "description": "Death evidence. Its year and place compare only with the candidate's death event.",
                  "properties": {
                    "year": {
                      "type": "integer",
                      "minimum": 1,
                      "maximum": 9999,
                      "description": "An exact or approximate death year. Exact matches score highest; one- and two-year differences receive partial credit."
                    },
                    "place": {
                      "type": "string",
                      "minLength": 1,
                      "pattern": "\\S",
                      "description": "A death place as free text. It compares only with the recorded death place using normalized containment."
                    }
                  }
                },
                "parents": {
                  "type": "object",
                  "additionalProperties": false,
                  "minProperties": 1,
                  "description": "Role-specific parent names. Use only a role the user identified; there is no unkeyed parent fallback.",
                  "properties": {
                    "father": {
                      "type": "string",
                      "minLength": 1,
                      "pattern": "\\S",
                      "description": "The father's name. It compares only with the candidate's recorded father."
                    },
                    "mother": {
                      "type": "string",
                      "minLength": 1,
                      "pattern": "\\S",
                      "description": "The mother's name. It compares only with the candidate's recorded mother."
                    }
                  }
                },
                "spouse": {
                  "type": "object",
                  "additionalProperties": false,
                  "minProperties": 1,
                  "description": "Evidence about one marriage. All supplied spouse and marriage leaves must be evaluated against the same candidate marriage; evidence is never combined across marriages.",
                  "properties": {
                    "name": {
                      "type": "string",
                      "minLength": 1,
                      "pattern": "\\S",
                      "description": "The spouse's name for the marriage being described."
                    },
                    "marriage": {
                      "type": "object",
                      "additionalProperties": false,
                      "minProperties": 1,
                      "description": "Date/place evidence for the same marriage as spouse.name. It may be supplied without a spouse name when only the marriage event is known.",
                      "properties": {
                        "year": {
                          "type": "integer",
                          "minimum": 1,
                          "maximum": 9999,
                          "description": "An exact or approximate marriage year. Exact matches score highest; one- and two-year differences receive partial credit."
                        },
                        "place": {
                          "type": "string",
                          "minLength": 1,
                          "pattern": "\\S",
                          "description": "A marriage place as free text, compared only with the place recorded on that marriage."
                        }
                      }
                    }
                  }
                }
              }
            },
            "maxResults": {
              "type": "integer",
              "minimum": 1,
              "maximum": 20,
              "default": 8,
              "description": "The most scored recall candidates to return, from 1 through 20. This never changes the matcher's confidence classification or totalMatches. Omit for the default of 8."
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
          "description": "One stable response shape for every lookup. matchType carries the confidence decision; candidates carries the requested scored recall set; scores are matcher evidence scores from 0 to 100, not probabilities.",
          "properties": {
            "matchType": {
              "type": "string",
              "enum": ["none", "single", "candidates"],
              "description": "The matcher's confidence classification: none means no name-recalled person, single means one decisive winner, and candidates means the recalled people remain ambiguous."
            },
            "confidentMatchXref": {
              "type": ["string", "null"],
              "pattern": "^@[^@]+@$",
              "description": "The selected person's stable GEDCOM xref when matchType is single; otherwise null."
            },
            "confidentMatchScore": {
              "type": ["number", "null"],
              "description": "The selected person's normalized evidence score when matchType is single; otherwise null. This is not a probability."
            },
            "person": {
              "$ref": "#/$defs/ResolvedPersonIdentity",
              "description": "Expanded identity and family handoff xrefs for a single confident match; null for none or candidates."
            },
            "candidates": {
              "type": "array",
              "maxItems": 20,
              "description": "The ordered scored name-recall set after maxResults is applied. It includes the winner first for a single match and is empty only for none.",
              "items": { "$ref": "#/$defs/CandidateIdentity" }
            },
            "suggestions": {
              "type": "array",
              "maxItems": 3,
              "description": "Up to three name-only near misses when matchType is none; empty for single or candidates. Suggestions did not clear the recall gate.",
              "items": { "$ref": "#/$defs/Suggestion" }
            },
            "totalMatches": {
              "type": "integer",
              "minimum": 0,
              "description": "The complete number of people admitted by the name-only recall gate before maxResults truncation."
            },
            "truncated": {
              "type": "boolean",
              "description": "Whether candidates contains fewer entries than totalMatches because maxResults capped the response."
            }
          },
          "required": [
            "matchType", "confidentMatchXref", "confidentMatchScore", "person",
            "candidates", "suggestions", "totalMatches", "truncated"
          ],
          "$defs": {
            "EventIdentity": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "description": "A recorded GEDCOM event summarized for identification, or null when neither date nor place is recorded.",
              "properties": {
                "date": {
                  "type": ["string", "null"],
                  "description": "The original GEDCOM date text, preserving qualifiers and ranges; null when absent."
                },
                "year": {
                  "type": ["integer", "null"],
                  "description": "The representative year parsed from date for comparison; null when no year can be parsed."
                },
                "qualifier": {
                  "type": ["string", "null"],
                  "description": "The GEDCOM date qualifier such as ABT, BEF, or AFT; null for an unqualified or absent date."
                },
                "place": {
                  "type": ["string", "null"],
                  "description": "The recorded event place as display text; null when absent."
                }
              },
              "required": ["date", "year", "qualifier", "place"]
            },
            "ParentsIdentity": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "description": "Names from the person's selected child-family roles, or null when neither parent is recorded.",
              "properties": {
                "father": {
                  "type": ["string", "null"],
                  "description": "The name referenced by the child family's HUSB role; null when absent."
                },
                "mother": {
                  "type": ["string", "null"],
                  "description": "The name referenced by the child family's WIFE role; null when absent."
                }
              },
              "required": ["father", "mother"]
            },
            "CandidateIdentity": {
              "type": "object",
              "additionalProperties": false,
              "description": "One recalled person with identification evidence and the score used to order the candidate set.",
              "properties": {
                "xref": {
                  "type": "string",
                  "pattern": "^@[^@]+@$",
                  "description": "The person's stable GEDCOM xref for follow-up tools."
                },
                "name": { "type": "string", "description": "The person's display name." },
                "birth": { "$ref": "#/$defs/EventIdentity", "description": "Recorded birth evidence." },
                "death": { "$ref": "#/$defs/EventIdentity", "description": "Recorded death evidence." },
                "parents": { "$ref": "#/$defs/ParentsIdentity", "description": "Recorded role-specific parent names." },
                "spouses": {
                  "type": "array",
                  "description": "Recorded spouse display names in the person's FAMS order.",
                  "items": { "type": "string" }
                },
                "matchScore": {
                  "type": "number",
                  "description": "The normalized name-and-available-hint evidence score used for ranking. This is not a probability."
                }
              },
              "required": ["xref", "name", "birth", "death", "parents", "spouses", "matchScore"]
            },
            "SpouseFamilyIdentity": {
              "type": "object",
              "additionalProperties": false,
              "description": "One family in which the resolved person is a spouse/parent, retained even when it has no children.",
              "properties": {
                "xref": {
                  "type": "string",
                  "pattern": "^@[^@]+@$",
                  "description": "The family's stable GEDCOM xref for family-detail or research tools."
                },
                "marriageDate": {
                  "type": ["string", "null"],
                  "description": "The original GEDCOM marriage date text; null when absent."
                },
                "spouseName": {
                  "type": ["string", "null"],
                  "description": "The other spouse's display name; null when no spouse record resolves."
                }
              },
              "required": ["xref", "marriageDate", "spouseName"]
            },
            "FamiliesIdentity": {
              "type": "object",
              "additionalProperties": false,
              "description": "Family xrefs that hand the resolved person off to family-oriented tools.",
              "properties": {
                "asChild": {
                  "type": "array",
                  "description": "The family in which this person is a child, empty when none resolves.",
                  "items": { "type": "string", "pattern": "^@[^@]+@$" }
                },
                "asParent": {
                  "type": "array",
                  "description": "Every family in which this person is a spouse/parent, in FAMS order, including childless marriages.",
                  "items": { "$ref": "#/$defs/SpouseFamilyIdentity" }
                }
              },
              "required": ["asChild", "asParent"]
            },
            "ResolvedPersonIdentity": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "description": "The expanded identity returned only for a single confident match.",
              "properties": {
                "xref": {
                  "type": "string",
                  "pattern": "^@[^@]+@$",
                  "description": "The person's stable GEDCOM xref for follow-up tools."
                },
                "name": { "type": "string", "description": "The person's display name." },
                "birth": { "$ref": "#/$defs/EventIdentity", "description": "Recorded birth evidence." },
                "death": { "$ref": "#/$defs/EventIdentity", "description": "Recorded death evidence." },
                "families": { "$ref": "#/$defs/FamiliesIdentity", "description": "Family handoff identifiers." }
              },
              "required": ["xref", "name", "birth", "death", "families"]
            },
            "Suggestion": {
              "type": "object",
              "additionalProperties": false,
              "description": "A name-only near miss that did not clear the recall gate.",
              "properties": {
                "xref": {
                  "type": "string",
                  "pattern": "^@[^@]+@$",
                  "description": "The suggested person's stable GEDCOM xref."
                },
                "name": { "type": "string", "description": "The suggested person's display name." },
                "reason": {
                  "type": "string",
                  "enum": ["close spelling", "partial name"],
                  "description": "Why this name was retained as a near miss."
                },
                "matchScore": {
                  "type": "number",
                  "description": "The name-only evidence score below the recall threshold. This is not a probability."
                }
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
    // it entirely (as most calls will).
    Task<CallToolResult> InvokeAsync(
      string query, FindPersonHintsArgs? hints = null, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
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
      string query, FindPersonHintsArgs? hints, CancellationToken cancellationToken, int maxResults = DefaultMaxResults)
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
    string query, FindPersonHintsArgs? hints, int maxResults, CancellationToken cancellationToken)
    {
        string trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0)
            return CallToolResults.Error("query must not be blank.");

    if (maxResults is < 1 or > MaximumMaxResults)
      return CallToolResults.Error($"maxResults must be an integer between 1 and {MaximumMaxResults}.");

        if (!TryValidateHints(hints, out string? hintsError))
          return CallToolResults.Error(hintsError!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var matcher = new PersonMatcher(_nicknames);
        // trimmed.Length > 0 here guarantees query is non-null; the matcher
        // still receives the original, untrimmed query (it normalizes on its
        // own terms) rather than the trimmed copy.
        var outcome = matcher.Match(snapshot.MatchIndex, query!, ToMatchHints(hints), maxResults);

        return CallToolResults.Success(MapOutcome(outcome), CallToolResults.JsonOptions);
    }

    static MatchHints ToMatchHints(FindPersonHintsArgs? hints) => hints is null
      ? MatchHints.None
      : new MatchHints(
        ToEventHint(hints.Birth),
        ToEventHint(hints.Death),
        hints.Parents is { } parents ? new ParentsHint(parents.Father, parents.Mother) : null,
        hints.Spouse is { } spouse
          ? new SpouseHint(spouse.Name, ToEventHint(spouse.Marriage))
          : null);

    static EventHint? ToEventHint(FindPersonEventHintArgs? hint) =>
      hint is null ? null : new EventHint(hint.Year, hint.Place);

    static bool TryValidateHints(FindPersonHintsArgs? hints, out string? error)
    {
      if (hints is null)
      {
        error = null;
        return true;
      }

      if (TryUnknownProperty("hints", hints.AdditionalProperties, out error)) return false;
      if (hints.Birth is null && hints.Death is null && hints.Parents is null && hints.Spouse is null)
      {
        error = "hints must contain at least one of birth, death, parents, or spouse.";
        return false;
      }

      if (!TryValidateEvent("hints.birth", hints.Birth, out error) ||
        !TryValidateEvent("hints.death", hints.Death, out error))
        return false;

      if (hints.Parents is { } parents)
      {
        if (TryUnknownProperty("hints.parents", parents.AdditionalProperties, out error)) return false;
        if (parents.Father is null && parents.Mother is null)
        {
          error = "hints.parents must contain father or mother.";
          return false;
        }
        if (!TryValidateText("hints.parents.father", parents.Father, out error) ||
          !TryValidateText("hints.parents.mother", parents.Mother, out error))
          return false;
      }

      if (hints.Spouse is { } spouse)
      {
        if (TryUnknownProperty("hints.spouse", spouse.AdditionalProperties, out error)) return false;
        if (spouse.Name is null && spouse.Marriage is null)
        {
          error = "hints.spouse must contain name or marriage.";
          return false;
        }
        if (!TryValidateText("hints.spouse.name", spouse.Name, out error) ||
          !TryValidateEvent("hints.spouse.marriage", spouse.Marriage, out error))
          return false;
      }

      error = null;
      return true;
    }

    static bool TryValidateEvent(string path, FindPersonEventHintArgs? hint, out string? error)
    {
      if (hint is null)
      {
        error = null;
        return true;
      }
      if (TryUnknownProperty(path, hint.AdditionalProperties, out error)) return false;
      if (hint.Year is null && hint.Place is null)
      {
        error = $"{path} must contain year or place.";
        return false;
      }
      if (hint.Year is < 1 or > 9999)
      {
        error = $"{path}.year must be between 1 and 9999.";
        return false;
      }
      return TryValidateText($"{path}.place", hint.Place, out error);
    }

    static bool TryValidateText(string path, string? value, out string? error)
    {
      if (value is not null && string.IsNullOrWhiteSpace(value))
      {
        error = $"{path} must not be blank.";
        return false;
      }
      error = null;
      return true;
    }

    static bool TryUnknownProperty(
      string path, Dictionary<string, JsonElement>? additionalProperties, out string? error)
    {
      if (additionalProperties is { Count: > 0 })
      {
        error = $"{path} contains unknown property '{additionalProperties.Keys.First()}'.";
        return true;
      }
      error = null;
      return false;
    }

    const int DefaultMaxResults = 8;
    const int MaximumMaxResults = 20;

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
  [JsonPropertyName("birth")]
  public FindPersonEventHintArgs? Birth { get; init; }

  [JsonPropertyName("death")]
  public FindPersonEventHintArgs? Death { get; init; }

  [JsonPropertyName("parents")]
  public FindPersonParentsHintArgs? Parents { get; init; }

  [JsonPropertyName("spouse")]
  public FindPersonSpouseHintArgs? Spouse { get; init; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class FindPersonEventHintArgs
{
  [JsonPropertyName("year")]
  public int? Year { get; init; }

  [JsonPropertyName("place")]
  public string? Place { get; init; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class FindPersonParentsHintArgs
{
  [JsonPropertyName("father")]
  public string? Father { get; init; }

  [JsonPropertyName("mother")]
  public string? Mother { get; init; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class FindPersonSpouseHintArgs
{
  [JsonPropertyName("name")]
  public string? Name { get; init; }

  [JsonPropertyName("marriage")]
  public FindPersonEventHintArgs? Marriage { get; init; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

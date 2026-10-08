namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// find_person's advertised input and output schemas.
// ---------------------------------------------------------------------------

public static class FindPersonSchemas
{
    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "The name as the user said it: a full name, given name, shortened prefix such as Fred for Frederick, a documented nickname such as Bill for William, or a close spelling. Pass it unchanged; the tool normalizes it. A part written Unknown (Mary Unknown, Unknown Pike) is a wildcard: it matches any value and adds no score, and people recorded with an Unknown part rank below exact name matches."
            },
            "hints": {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 1,
              "description": "Structured facts the user mentioned, used only to rank and narrow people already recalled by query. Omit unknown facts and omit hints entirely when none are known. Empty objects, blank strings, legacy flat properties, and unknown properties are invalid. Missing candidate data is not penalized.",
              "properties": {
                "sex": {
                  "type": "string",
                  "enum": ["M", "F"],
                  "description": "The sought person's sex. A candidate whose recorded sex differs ranks lower; one with no recorded sex is neither helped nor penalized. It never removes a candidate."
                },
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
                },
                "surnameUnknown": {
                  "type": "boolean",
                  "description": "True when the person's surname is recorded as Unknown. The surname then added no evidence to matchScore."
                }
              },
              "required": ["xref", "name", "birth", "death", "parents", "spouses", "matchScore", "surnameUnknown"]
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
                "families": { "$ref": "#/$defs/FamiliesIdentity", "description": "Family handoff identifiers." },
                "surnameUnknown": {
                  "type": "boolean",
                  "description": "True when the person's surname is recorded as Unknown. The surname then added no evidence to the match."
                }
              },
              "required": ["xref", "name", "birth", "death", "families", "surnameUnknown"]
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
}

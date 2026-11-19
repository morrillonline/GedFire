using System.Text.Json.Nodes;

namespace GedFire.Schemas;

// ---------------------------------------------------------------------------
// The JSON Schema fragments of the finding-file contract, shared by the tool
// input schemas that accept them (draft_changeset's findings, match_record's
// record) and the standalone documents SchemaCatalog publishes, so the two
// cannot drift apart.
// ---------------------------------------------------------------------------

public static class FindingSchemas
{
    /// <summary>The identity argument a finding carries.</summary>
    public const string ArgumentJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "description": "The identity argument correlation wrote for this finding: why this person and not a same-named rival. Carried unchanged into the changeset's findings table for draft_package to present; never judged and never applied. Every field is optional.",
          "properties": {
            "anchors": { "type": "array", "items": { "type": "string" }, "description": "Why him: the anchoring facts, one sentence each." },
            "identity": { "type": "string", "description": "The sentence the author wrote saying how the identity was resolved, e.g. the find_person survey; presented as written." },
            "exclusions": {
              "type": "array",
              "description": "Why not others: one entry per excluded rival.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": { "name": { "type": "string" }, "xref": { "type": "string" }, "fact": { "type": "string" }, "finding": { "type": "string" } }
              }
            },
            "cautions": { "type": "string", "description": "Conflicts weighed; omit for none." },
            "difficulty": { "type": "string", "enum": ["low", "medium", "high"] }
          }
        }
        """;

    /// <summary>One draft_changeset findings entry; <see cref="FindingJson"/> adds its argument.</summary>
    const string FindingWithoutArgumentJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "id": { "type": "string", "description": "The finding's identifier, echoed in decisions." },
            "extract": { "type": "string", "description": "The verbatim extract cited as the citation's dataText unless a fact gives its own excerpt." },
            "source": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "ref": { "type": "string", "pattern": "^@[^@]+@$", "description": "An existing source record's xref, cited instead of the source the name and author would find; use it when the document holds several sources with this title and author. name (and author, when given) must agree with the record's." },
                "name": { "type": "string", "description": "The collection, e.g. \"Find a Grave\"; one source record is made per name and author." },
                "author": { "type": "string" },
                "url": { "type": "string", "description": "Written on the source record only when every finding of the collection gives the same one." },
                "locator": { "type": "string", "minLength": 1, "description": "Copied verbatim into each citation's page. Omit it only to add to a held citation of this source that has no page: every fact then has citeOnly, and the fact it selects must already cite the source without a page." },
                "accessed": { "type": "string" },
                "class": { "type": "string", "enum": ["original", "image copy", "derivative", "authored", "user-contributed"] },
                "information": { "type": "string", "enum": ["primary", "secondary", "undetermined"] }
              },
              "required": ["name", "class", "information"]
            },
            "target": {
              "type": "object",
              "additionalProperties": false,
              "description": "The person the work is for; findings with the same xref and card become one item.",
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "card": { "type": "string" },
                "name": { "type": "string", "description": "Checked against the name recorded for the xref." }
              },
              "required": ["xref"]
            },
            "facts": {
              "type": "array",
              "description": "The facts to assert, each graded on its own.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "record": { "type": "string", "pattern": "^@[^@]+@$", "description": "The person or family the fact belongs to." },
                  "fact": { "type": "string", "description": "The GEDCOM tag, e.g. BIRT, DEAT, BURI, MARR; describe_changeset_ops lists every tag createOrUpdateVital takes from a draft. A family event (MARR, DIV, ENGA, …) goes on the couple's family, never a person; on a person it may only be withdrawn." },
                  "date": { "type": "string", "description": "A GEDCOM 7 date, e.g. \"1 JUN 1840\", \"ABT 1840\", \"BET 1840 AND 1845\" or \"FROM 1840 TO 1845\"; case is ignored." },
                  "place": { "type": "string" },
                  "value": { "type": "string", "description": "A text fact's payload, e.g. a NAME change (\"Ralph /Morrill/\"); not combined with date or place." },
                  "allowSurnameChange": { "type": "boolean", "description": "Explicit permission for this NAME value to change the existing person's surname; default false. Only applies to NAME assertions with a value." },
                  "quay": { "type": "integer", "minimum": 0, "maximum": 3 },
                  "class": { "type": "string", "enum": ["original", "image copy", "derivative", "authored", "user-contributed"], "description": "Overrides the source's class for this fact." },
                  "information": { "type": "string", "enum": ["primary", "secondary", "undetermined"], "description": "Overrides the source's information class for this fact." },
                  "excerpt": { "type": "string", "description": "A narrower extract for this fact's citation, quoted word for word from extract." },
                  "substructures": {
                    "type": "array",
                    "description": "Extra lines under the fact, e.g. [{\"tag\": \"TYPE\", \"value\": \"Pension Application\"}] on an EVEN. A fact with substructures is added, never matched by date.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": { "tag": { "type": "string" }, "value": { "type": "string" } },
                      "required": ["tag", "value"]
                    }
                  },
                  "replacedCitations": { "type": "string", "enum": ["keep", "drop"], "description": "What becomes of the citations on a fact whose value this replaces. Default keep for a value that refines the old one; drop for a contradicted value replaced by evidence or replaceHeld, and for NAME, which refuses keep. A corrected value and its citations leave the record." },
                  "citeOnly": { "type": "boolean", "description": "Only add this finding's citation to a fact the record already holds. date and place then select the instance when the record has several." },
                  "replaceHeld": { "type": "string", "minLength": 1, "description": "The reason to write this value over the one the record holds, even a more specific or better-cited one (a single-valued fact). A contradicted value's citations are removed unless replacedCitations says otherwise." },
                  "withdraw": { "type": "string", "minLength": 1, "description": "The reason to delete the fact the record holds, with nothing in its place; date and place select the instance. Its citations are removed with it. The reason and this finding's evidence travel in the changeset's findings table for the reviewer; nothing about the removed value is written into a note." },
                  "cites": {
                    "type": "array",
                    "description": "Other findings whose records also support this fact, whatever their target or status: another finding drafted with this one, or one staged as <id>.json beside this file. The fact keeps its own finding's citation at quay and gains one citation per entry, carrying the cited finding's source, locator and extract (or excerpt); nothing else is drafted from the cited finding. Not with withdraw.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "finding": { "type": "string", "minLength": 1, "description": "The id of the finding cited; not this fact's own finding." },
                        "quay": { "type": "integer", "minimum": 0, "maximum": 3, "description": "The grade of the cited record for this fact, at most its source's ceiling. One quoted text carries one grade, so a differently graded cite needs its own excerpt." },
                        "excerpt": { "type": "string", "description": "A narrower extract than the cited finding's, quoted word for word from it." },
                        "commentary": { "type": "string", "minLength": 1, "pattern": "^[^\\r\\n]*$", "description": "A sentence or two on what this cited record shows for the fact, printed after its source in the fact's footnote. No record ids." }
                      },
                      "required": ["finding", "quay"]
                    }
                  },
                  "commentary": { "type": "string", "minLength": 1, "pattern": "^[^\\r\\n]*$", "description": "A sentence or two of the author's comment on this finding's evidence for the fact, such as why its reading is preferred over another record's, or that a date is derived from an age. Printed after the source in the fact's footnote; discussion of evidence belongs here, never in a note. No record ids. Not with withdraw." },
                  "commentaryOnly": { "type": "boolean", "description": "Add only commentary to the fact the record holds, as a comment citing no source (SOUR @VOID@); date and place select the instance. Nothing is asserted and this finding's source is not cited, so quay, value, excerpt, cites and the other claim fields do not apply." }
                },
                "required": ["record", "fact"],
                "if": { "properties": { "commentaryOnly": { "const": true } }, "required": ["commentaryOnly"] },
                "then": { "required": ["commentary"] },
                "else": { "required": ["quay"] }
              }
            },
            "existingPeople": {
              "type": "array",
              "description": "Existing people explicitly bound by xref, without creating a person or asserting a relationship. Targets and person references in relations also bind people; a facts[].record alone does not.",
              "items": { "type": "string", "pattern": "^@[^@]+@$" }
            },
            "people": {
              "type": "array",
              "description": "New people this finding adds, each named by a placeholder xref. A person is created by the relation that links them; give their facts under facts with the placeholder as record.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "xref": { "type": "string", "pattern": "^@New[A-Za-z0-9_]+@$", "description": "A placeholder such as @NewI1@; apply mints the real xref." },
                  "name": { "type": "string", "description": "GEDCOM form, e.g. \"Sarah /Blake/\"." },
                  "sex": { "type": "string", "enum": ["M", "F"], "description": "Required for a person a relation places as spouse, parent, husb or wife, so the role can be typed." }
                },
                "required": ["xref", "name"]
              }
            },
            "relations": {
              "type": "array",
              "description": "Links this finding establishes or, with the unlink kinds, removes, in the changeset dialect's terms. Each added link is cited on its family with the finding's source at its own quay, and with each finding it cites; a child relation with from moves an existing child between families. Family xrefs for new families are placeholders such as @NewF1@; facts about the family (MARR) use that xref as record.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "kind": { "type": "string", "enum": ["spouse", "child", "parent", "unlink-child", "unlink-parent"], "description": "unlink-child and unlink-parent remove an existing link; they take no quay, excerpt, cites or commentary, because a removal op carries no citation." },
                  "person": { "type": "string", "description": "spouse, parent and unlink-parent: the known person (the child, for parent and unlink-parent). An existing xref, or a person an earlier relation created." },
                  "spouse": { "type": "string", "description": "spouse: the partner, existing or declared in people." },
                  "child": { "type": "string", "description": "child and unlink-child: the child, existing or declared in people (an existing person for unlink-child, or for a child with from)." },
                  "parent": { "type": "string", "description": "parent: the parent, existing or declared in people." },
                  "role": { "type": "string", "enum": ["father", "mother"], "description": "parent and unlink-parent: which parent." },
                  "family": { "type": "string", "description": "The family: required for spouse, child and unlink-child (the family the child leaves), optional for parent and unlink-parent. A placeholder creates it, except for the removals." },
                  "from": { "type": "string", "description": "child: an existing family the child is moved out of, so the child ends up in family only. The child must be listed in it." },
                  "husb": { "type": "string", "description": "child: father, used only when this relation creates the family." },
                  "wife": { "type": "string", "description": "child: mother, used only when this relation creates the family." },
                  "notDuplicateOf": { "type": "array", "items": { "type": "string" }, "description": "spouse, child and parent: existing people already confirmed not to be the new person. A draft is refused when the person it attaches to already has a parent in the same role, or a spouse or child whose name agrees (the same surname and a given name in common, after abbreviations such as Wm are expanded); listing that relative here lets it through, for example to replace a parent knowingly." },
                  "excerpt": { "type": "string", "description": "A narrower extract than the finding's for this relation's citation, quoted word for word from extract." },
                  "quay": { "type": "integer", "minimum": 0, "maximum": 3, "description": "Required except for unlink-child, unlink-parent and commentaryOnly." },
                  "cites": {
                    "type": "array",
                    "description": "spouse, child and parent: other findings whose records also support this link, whatever their target or status: another finding drafted with this one, or one staged as <id>.json beside this file. The link keeps its own finding's citation at quay and its family gains one citation per entry, carrying the cited finding's source, locator and extract (or excerpt); nothing else is drafted from the cited finding.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "finding": { "type": "string", "minLength": 1, "description": "The id of the finding cited; not this relation's own finding." },
                        "quay": { "type": "integer", "minimum": 0, "maximum": 3, "description": "The grade of the cited record for this link, at most its source's ceiling. One quoted text carries one grade, so a differently graded cite needs its own excerpt." },
                        "excerpt": { "type": "string", "description": "A narrower extract than the cited finding's, quoted word for word from it." },
                        "commentary": { "type": "string", "minLength": 1, "pattern": "^[^\\r\\n]*$", "description": "A sentence or two on what this cited record shows for the link, printed after its source in the family's footnote. No record ids." }
                      },
                      "required": ["finding", "quay"]
                    }
                  },
                  "commentary": { "type": "string", "minLength": 1, "pattern": "^[^\\r\\n]*$", "description": "spouse, child and parent: a sentence or two of the author's comment on this finding's evidence for the link, such as an index misreading a name the image shows, written on the family citation and printed after the source in its footnote. Commentary that several findings of one item give the same source and page on one family is joined. No record ids." },
                  "commentaryOnly": { "type": "boolean", "description": "spouse, child and parent: add only commentary to the family of a link the tree already holds, as a comment citing no source (SOUR @VOID@). Nothing is asserted and this finding's source is not cited, so quay, excerpt, cites, notDuplicateOf, from, husb and wife do not apply. Refused when the tree does not hold the link." }
                },
                "required": ["kind"],
                "if": { "properties": { "commentaryOnly": { "const": true } }, "required": ["commentaryOnly"] },
                "then": { "required": ["commentary"] }
              }
            },
            "notes": {
              "type": "array",
              "description": "Notes you wrote, passed through unchanged and checked against the policy.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "record": { "type": "string", "pattern": "^@[^@]+@$" },
                  "text": { "type": "string" },
                  "match": { "type": "string", "description": "The exact text of an existing note on the record, to rewrite instead of adding a new note, or to delete with withdraw." },
                  "withdraw": { "type": "string", "minLength": 1, "description": "The reason to delete the note match names, with nothing in its place. Takes match and no text, cites or purpose; a match the record does not hold drafts nothing and is reported as an open decision." },
                  "purpose": { "type": "string", "enum": ["biography"], "description": "biography: the note is the person's cited biographical prose (service, occupation, migration, probate...), compiled to a createOrUpdateNote with purpose biography so the new information it adds (a citation or text none of the person's notes carried) earns biography credit. A biography note needs at least one cite. Biography notes are joined only with other biography notes." },
                  "cites": {
                    "type": "array",
                    "description": "Citations on the note. Each cites a finding by id (the note's own finding when finding is omitted), graded on its own: any finding, whatever its target or status, either drafted with this one or staged as <id>.json beside this file. A cited finding lends only its source, locator and extract (or excerpt); it is not drafted because it is cited.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "finding": { "type": "string", "description": "The id of the finding cited; default the note's own." },
                        "quay": { "type": "integer", "minimum": 0, "maximum": 3 },
                        "excerpt": { "type": "string", "description": "A narrower extract than the cited finding's, quoted word for word from it." },
                        "commentary": { "type": "string", "minLength": 1, "pattern": "^[^\\r\\n]*$", "description": "A sentence or two on what this cited record shows, printed after its source in the note's footnote. No record ids." }
                      },
                      "required": ["quay"]
                    }
                  }
                },
                "required": ["record"],
                "allOf": [
                  {
                    "if": { "properties": { "purpose": { "const": "biography" } }, "required": ["purpose"] },
                    "then": { "properties": { "cites": { "minItems": 1 } }, "required": ["cites"] }
                  },
                  {
                    "if": { "required": ["withdraw"] },
                    "then": { "required": ["match"], "not": { "anyOf": [{ "required": ["text"] }, { "required": ["cites"] }, { "required": ["purpose"] }] } },
                    "else": { "required": ["text"] }
                  }
                ]
              }
            }
          },
          "required": ["id", "extract", "source", "target"]
        }
        """;

    /// <summary>One draft_changeset findings entry, including its argument.</summary>
    public static readonly string FindingJson = SchemaComposer.Compose(FindingWithoutArgumentJson, finding =>
        finding["properties"]!["argument"] = JsonNode.Parse(ArgumentJson));

    /// <summary>match_record's persons: every person the record names.</summary>
    public const string RecordPersonsJson = """
        {
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
              "birthPlace": { "type": "string", "minLength": 1 },
              "deathYear": { "type": "integer", "minimum": 1, "maximum": 9999, "description": "The death year the record states, e.g. on a death record or obituary. Kept with the record; not yet scored." },
              "maidenName": { "type": "string", "minLength": 1, "description": "The maiden name the record states. Kept with the record; not yet scored." },
              "xref": { "type": "string", "pattern": "^@[^@]+@$", "description": "The tree person this record person is already bound to. match_record reports them as single on this xref and uses the binding to score the record's other people." }
            },
            "required": ["name"]
          },
          "description": "Every person the record names."
        }
        """;

    /// <summary>match_record's relationships, by index into persons.</summary>
    public const string RecordRelationshipsJson = """
        {
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
        }
        """;

    /// <summary>match_record's event: the record's own date and place.</summary>
    public const string RecordEventJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "date": { "type": "string", "minLength": 1, "description": "The GEDCOM 7 date of the record's own event, e.g. \"1880\" or \"12 JUN 1880\". Must contain a year." },
            "place": { "type": "string", "minLength": 1 },
            "type": { "type": "string", "minLength": 1, "description": "Free text, e.g. \"census\" or \"obituary\". Context only; never scored." }
          }
        }
        """;

    /// <summary>The record a finding was read from, exactly as match_record takes it.</summary>
    public static readonly string RecordJson = SchemaComposer.Compose("""
        {
          "type": "object",
          "additionalProperties": false,
          "description": "The record as match_record input: its persons, the relationships it states, and its own event. match_record answers each person with a verdict: single (a match the name and at least one of lifespan, place or kin support), weak (a lead on the name alone, or someone the tree does not hold as a bound person's relative of the stated kind), candidates, or none.",
          "properties": {},
          "required": ["persons"]
        }
        """, AddRecordProperties);

    /// <summary>Adds persons, relationships and event to a schema's properties.</summary>
    public static void AddRecordProperties(JsonNode schema)
    {
        var properties = schema["properties"]!;
        properties["persons"] = JsonNode.Parse(RecordPersonsJson);
        properties["relationships"] = JsonNode.Parse(RecordRelationshipsJson);
        properties["event"] = JsonNode.Parse(RecordEventJson);
    }
}

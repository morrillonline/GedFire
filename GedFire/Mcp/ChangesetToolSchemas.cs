using System.Text.Json.Nodes;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The advertised input and output schemas of the changeset tools. validate_changeset
// extends the shared pair with its optional cards argument and pointsPreview result.
// ---------------------------------------------------------------------------

static class ChangesetToolSchemas
{
    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "changesetPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Path to the changeset JSON file -- the machine-applicable side of a research proposal, in the v2 op dialect (call describe_changeset_ops for its full shape). Absolute, or relative to the server process's working directory."
            },
            "items": {
              "type": "string",
              "minLength": 1,
              "pattern": "^(all|[0-9]+(,[0-9]+)*)$",
              "description": "Which changeset items to include: the literal \"all\", or a comma-separated list of item numbers such as \"1,3\". Excluding an item also excludes any newSources[] group only that item cites."
            }
          },
          "required": ["changesetPath", "items"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": {
              "type": "boolean",
              "description": "Whether validation (and, for apply_changeset, the write) succeeded. false means errors explains why, and the file -- if any -- is untouched."
            },
            "log": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Human-readable trace of what was checked or applied, in order."
            },
            "errors": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Validation or verification failures. Non-empty only when success is false."
            },
            "deltas": {
              "type": "object",
              "additionalProperties": { "type": "integer" },
              "description": "Signed per-record-tag count changes actually written, e.g. {\"INDI\": 1}. Empty for validate_changeset, and for an apply_changeset run whose ops were all no-ops."
            },
            "mintedXrefs": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Placeholder token to real xref for every new record this run created, or for an existing source that is the same document as a new one described (reused, not created). Empty for validate_changeset."
            },
            "copiedSources": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Original source xref to the source used in its place, for every shared source (cited by more than one structure) that an item updated: a new copy carrying the update, or an existing source that is exactly the updated one. The original is never changed. Empty when no shared source was updated, and for validate_changeset."
            }
          },
          "required": ["success", "log", "errors", "deltas", "mintedXrefs", "copiedSources"]
        }
        """;

    /// <summary>The input schema with the optional <c>cards</c> argument validate_changeset adds.</summary>
    public static readonly string InputSchemaWithCardsJson = Compose(InputSchemaJson, schema =>
        schema["properties"]!["cards"] = JsonNode.Parse("""
            {
              "type": "array",
              "description": "Research cards the author is working. When given, the result carries pointsPreview: which cards this changeset closes by what it actually writes to the tree, which it leaves open and why, and any card whose subject the changeset renamed. An enrich card closes only when the fact it names is written on its subject; a citation alone writes no fact.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "xref": { "type": "string", "pattern": "^@[^@]+@$", "description": "The card subject's xref." },
                  "class": { "type": "string", "description": "enrich-person, new-parent, new-spouse or new-child (select_targets' card type, case and spacing ignored)." },
                  "gap": { "type": "string", "description": "For enrich-person, the fact the card names: enrich-birth, enrich-death or enrich-marriage, optionally ending -date or -place. Omit it, or use enrich-fact, for a card that names no single fact: it closes on any improved fact written on the subject." },
                  "name": { "type": "string", "description": "For enrich-person, the subject's name: a changeset that renames the subject to something else leaves the card open. For new-parent, new-spouse and new-child, the name of the person to mint: a person the changeset creates must carry exactly this name, or the card stays open." },
                  "points": { "type": "integer", "minimum": 0, "description": "The card's points. For a card with no single named fact (no gap, or enrich-fact) this is the points for each improved date or place, and closed.points is this times the improvements plus bonus." },
                  "bonus": { "type": "integer", "minimum": 0, "description": "The card's difficulty bonus, added once to a card that names no single fact; ignored for other cards, whose points already include it." }
                },
                "required": ["xref", "class", "points"]
              }
            }
            """));

    /// <summary>The output schema with the optional <c>pointsPreview</c> validate_changeset returns when cards are given.</summary>
    public static readonly string OutputSchemaWithPreviewJson = Compose(OutputSchemaJson, schema =>
        schema["properties"]!["pointsPreview"] = JsonNode.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "description": "Present only when cards were given.",
              "properties": {
                "closed": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "cardKey": { "type": "string" }, "points": { "type": "integer" }, "wrote": { "type": "string", "description": "The fact or relationship the changeset wrote that closes the card." } },
                  "required": ["cardKey", "points", "wrote"] } },
                "notClosed": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "cardKey": { "type": "string" }, "reason": { "type": "string" } },
                  "required": ["cardKey", "reason"] } },
                "subjectMismatch": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "cardKey": { "type": "string" }, "cardSubject": { "type": "string" }, "appliedName": { "type": "string" } },
                  "required": ["cardKey", "cardSubject", "appliedName"] } },
                "total": { "type": "integer", "description": "Points of the closed cards." }
              },
              "required": ["closed", "notClosed", "subjectMismatch", "total"]
            }
            """));

    static string Compose(string baseSchema, Action<JsonNode> addTo)
    {
        var schema = JsonNode.Parse(baseSchema)!;
        addTo(schema);
        return schema.ToJsonString();
    }
}

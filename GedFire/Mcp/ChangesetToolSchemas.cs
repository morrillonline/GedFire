using System.Text.Json.Nodes;
using GedFire.Schemas;

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
          "description": "The full result, or -- when reportPath was given without verbose -- a digest of its counts, with the full result in the report file.",
          "properties": {
            "success": {
              "type": "boolean",
              "description": "Whether validation (and, for apply_changeset, the write) succeeded. false means errors explains why, and the file -- if any -- is untouched."
            },
            "log": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Human-readable trace of what was checked or applied, in order. Absent from the digest; the report file holds it."
            },
            "errors": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Validation or verification failures. Non-empty only when success is false. Absent from the digest; the report file holds it."
            },
            "deltas": {
              "type": "object",
              "additionalProperties": { "type": "integer" },
              "description": "Signed per-record-tag count changes actually written, e.g. {\"INDI\": 1}. Empty for validate_changeset, and for an apply_changeset run whose ops were all no-ops."
            },
            "mintedXrefs": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Placeholder token to real xref for every new record this run created, or for an existing source that is the same document as a new one described (reused, not created). Empty for validate_changeset. Absent from the digest; the report file holds it."
            },
            "copiedSources": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Original source xref to the source used in its place, for every shared source (cited by more than one structure) that an item updated: a new copy carrying the update, or an existing source that is exactly the updated one. The original is never changed. Empty when no shared source was updated, and for validate_changeset. Absent from the digest; the report file holds it."
            },
            "items": {
              "type": "array",
              "items": { "type": "integer" },
              "description": "Digest only: the changeset item numbers the run selected."
            },
            "errorCount": { "type": "integer", "description": "Digest only: how many errors the report file holds." },
            "logCount": { "type": "integer", "description": "Digest only: how many log lines the report file holds." },
            "mintedXrefCount": { "type": "integer", "description": "Digest only: how many records the run minted; the report file names them." },
            "reportWrittenTo": {
              "type": ["string", "null"],
              "description": "The full path the result was also written to when reportPath was given, else null. A report that could not be written is noted in log and does not change the outcome."
            }
          },
          "required": ["success", "reportWrittenTo"]
        }
        """;

    /// <summary>The input schema with the optional <c>reportPath</c> and <c>overwrite</c> arguments.</summary>
    public static readonly string InputSchemaWithReportJson = SchemaComposer.Compose(InputSchemaJson, AddReportProperties);

    static void AddReportProperties(JsonNode schema)
    {
        schema["properties"]!["reportPath"] = JsonNode.Parse("""
            {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Write the full result JSON to this file and return only a digest of counts inline (pass verbose: true for both), so a script can read the exact result without it also filling the caller's context.               The folder must exist. Refused, before any work is done, when it is this server's GEDCOM file, or when the file already exists unless overwrite is true."
            }
            """);
        schema["properties"]!["overwrite"] = JsonNode.Parse("""
            {
              "type": "boolean",
              "description": "Replace an existing file at reportPath. Default false."
            }
            """);
        schema["properties"]!["verbose"] = JsonNode.Parse("""
            {
              "type": "boolean",
              "description": "With reportPath, return the full result inline as well as writing the file, instead of the digest. Default false; ignored without reportPath."
            }
            """);
    }

    /// <summary>The input schema with the optional <c>after</c> argument check_plausibility adds.</summary>
    public static readonly string InputSchemaWithAfterJson = SchemaComposer.Compose(InputSchemaJson, AddAfterProperty);

    static void AddAfterProperty(JsonNode schema) =>
        schema["properties"]!["after"] = JsonNode.Parse(PriorChangesets.SchemaJson);

    /// <summary>One research card, in validate_changeset's cards and in a cards file.</summary>
    public const string CardJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "xref": { "type": "string", "pattern": "^@[^@]+@$", "description": "The card subject's xref." },
            "class": { "type": "string", "description": "enrich-person, new-parent, new-spouse, new-child or biography (select_targets' card type, case and spacing ignored)." },
            "gap": { "type": "string", "description": "For enrich-person, the fact the card names: enrich-birth, enrich-death or enrich-marriage, optionally ending -date or -place. Omit it, or use enrich-fact, for select_targets' Enrich person card, which names no single fact: it is paid for each gap select_targets listed for the subject that the changeset closes." },
            "name": { "type": "string", "description": "For enrich-person, the subject's name: a changeset that renames the subject to something else leaves the card open. For new-parent, new-spouse and new-child, the name of the person to mint: a person the changeset creates must carry exactly this name, or the card stays open." },
            "family": { "type": "string", "pattern": "^@[^@]+@$", "description": "For new-child, the family the card was dealt for (select_targets' spouseFamily.familyXref): only a child joining this family closes the card, and closed and notClosed repeat it. Without it the card stands for any family of the subject that was childless before the changeset." },
            "points": { "type": "integer", "minimum": 0, "description": "The card's points. For a card with no single named fact (no gap, or enrich-fact) this is the points for each gap closed, and closed.points is this times the gaps closed plus bonus." },
            "bonus": { "type": "integer", "minimum": 0, "description": "The card's difficulty bonus, added once to a card that names no single fact; ignored for other cards, whose points already include it." }
          },
          "required": ["xref", "class", "points"]
        }
        """;

    /// <summary>The input schema with the optional <c>cards</c> argument validate_changeset adds.</summary>
    public static readonly string InputSchemaWithCardsJson = SchemaComposer.Compose(InputSchemaJson, schema =>
    {
        AddReportProperties(schema);
        AddAfterProperty(schema);
        schema["properties"]!["cardsPath"] = JsonNode.Parse("""
            {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Path to a JSON file holding {\"cards\": [...]}, the same array as cards, so a saved list need not be passed back inline (gedfire schema cards). Give cards or cardsPath, not both."
            }
            """);
        schema["properties"]!["cards"] = JsonNode.Parse("""
            {
              "type": "array",
              "description": "Research cards the author is working. When given, the result carries pointsPreview: which cards this changeset closes by what it actually writes to the tree, which it leaves open and why, and any card whose subject the changeset renamed. An enrich card that names a fact closes only when that fact is written on its subject; a citation alone writes no fact. An enrich card that names none (select_targets' Enrich person card: no gap, or enrich-fact) is paid by the rule select_targets priced it by: its points for each missing date, place or citation select_targets listed on the subject's birth, death or marriages that the changeset fills (an exact date or an honest cited range, an exact place, a first citation), so filling exactly the listed gaps pays the dealt score; a residence, a burial that does not stand for the death, the marriage of a family the changeset creates, a date or place still not exact, and any other write select_targets did not list pay nothing; a family's marriage is paid to one card. A new-child card closes only on a child joining a family that existed before the changeset, childless, with the subject a partner in it (the card's family when it names one), and a family's New child card is paid once however many children or cards there are. A new-spouse card closes on a spouse, or on a cited NMR 0 (createOrUpdateVital, fact NMR, value \"0\") recorded on the subject. A biography card closes when a createOrUpdateNote with purpose biography adds new information to the subject: a citation (same source and page) none of the subject's biography notes carried before, or note text that matches none of the subject's notes once case, whitespace and punctuation are folded (rewording is not judged); one biography card per person closes per changeset."
            }
            """);
        schema["properties"]!["cards"]!["items"] = JsonNode.Parse(CardJson);
        schema["properties"]![StudyScopeArgument.Name] = JsonNode.Parse("""
            {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Only with cards: the surnames of a surname study, as select_targets takes them. A card select_targets would withhold as outside the study's scope is never closed; it is listed in notClosed with a reason starting \"out of study scope:\". Omit it to price every card."
            }
            """);
    });

    /// <summary>The <c>pointsPreview</c> schema, shared by every tool that prices a changeset against cards.</summary>
    public const string PointsPreviewJson = """
            {
              "type": "object",
              "additionalProperties": false,
              "description": "Present only when cards were given.",
              "properties": {
                "closed": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "cardKey": { "type": "string" }, "points": { "type": "integer" }, "wrote": { "type": "string", "description": "The fact or relationship the changeset wrote that closes the card." }, "family": { "type": "string", "description": "The card's family; present only when the card gave one." } },
                  "required": ["cardKey", "points", "wrote"] } },
                "notClosed": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "cardKey": { "type": "string" }, "reason": { "type": "string" }, "family": { "type": "string", "description": "The card's family; present only when the card gave one." } },
                  "required": ["cardKey", "reason"] } },
                "subjectMismatch": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "cardKey": { "type": "string" }, "cardSubject": { "type": "string" }, "appliedName": { "type": "string" } },
                  "required": ["cardKey", "cardSubject", "appliedName"] } },
                "total": { "type": "integer", "description": "Points of the closed cards." }
              },
              "required": ["closed", "notClosed", "subjectMismatch", "total"]
            }
            """;

    /// <summary>The output schema with the optional <c>pointsPreview</c> validate_changeset returns when cards are given.</summary>
    public static readonly string OutputSchemaWithPreviewJson = SchemaComposer.Compose(OutputSchemaJson, schema =>
    {
        schema["properties"]!["pointsPreview"] = JsonNode.Parse(PointsPreviewJson);
        schema["properties"]!["pointsPreviewTotal"] = JsonNode.Parse("""
            { "type": "integer", "description": "Digest only, and only when cards were given: pointsPreview's total; the report file holds the full preview." }
            """);
    });
}

using System.Text.Json;

namespace GedFire.Mcp;

/// <summary>One field of one changeset op, as reported by describe_changeset_ops.</summary>
public sealed record ChangesetOpField(string Name, string Type, bool Required, string Description);

/// <summary>One changeset op kind's shape: its fields and one worked example.</summary>
public sealed record ChangesetOpDescriptor(
    string Op, string Verb, string Noun, string Summary,
    IReadOnlyList<ChangesetOpField> Fields, JsonElement Example);

/// <summary>The changeset file's own envelope (proposal/newSources/items), independent of any op.</summary>
public sealed record ChangesetEnvelope(string Description, JsonElement Example);

public sealed record DescribeChangesetOpsResult(ChangesetEnvelope Envelope, IReadOnlyList<ChangesetOpDescriptor> Ops);

// ---------------------------------------------------------------------------
// Static reference data for describe_changeset_ops: one entry per ChangeOp
// subclass in GedCore.Apply.Ops. Kept next to, but not generated from,
// ChangeOp.ReadOp's switch (GedFire.Mcp does not reference GedCore.Apply's
// internal Read() methods) -- when a op's fields change there, this catalog
// needs the matching edit here. ChangesetToolTests.DescribeChangesetOpsTests
// cross-checks the "op" list against ChangeOp.ReadOp's own switch so the two
// cannot silently drift apart.
// ---------------------------------------------------------------------------

public static class ChangesetOpsCatalog
{
    static JsonElement Ex(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static readonly ChangesetEnvelope Envelope = new(
        "The whole changeset file's shape, independent of any one op. \"proposal\" is an optional free-text " +
        "label. \"newSources\" groups are optional: each names the xref its ops define; a group is applied " +
        "unconditionally unless some item in the changeset cites its xref, in which case it applies only when " +
        "a selected item (per the \"items\" argument) cites it -- so excluding an item also excludes a source " +
        "only that item needed. \"items\" is the numbered list validate_changeset/apply_changeset's own " +
        "\"items\" argument selects from (\"all\", or e.g. \"1,3\"); item numbers need not be contiguous or " +
        "sorted, and \"target\" is a free-text label for a human reviewer, not read by either tool.",
        Ex("""
            {
              "proposal": "mercy-whitfield-death-record",
              "newSources": [
                { "xref": "@NewSource1@", "ops": [
                  { "op": "createOrUpdateSource", "xref": "@NewSource1@",
                    "title": "Death certificate, Mercy (Whitfield) Ash, 1870",
                    "auth": "New Hampshire Bureau of Vital Records" } ] }
              ],
              "items": [
                { "item": 1, "target": "@I2@ Mercy Whitfield -> cite her death, currently uncited",
                  "ops": [
                    { "op": "createOrUpdateVital", "record": "@I2@", "fact": "DEAT",
                      "value": { "date": "19 JAN 1870", "place": "Salisbury, New Hampshire" },
                      "citation": { "source": "@NewSource1@", "page": "cert. 1870-0114",
                                    "dataText": "Mercy Ash, widow of Nathaniel, died 19 Jan 1870, aged 75",
                                    "quay": 2 } } ] }
              ]
            }
            """));

    // Shared field-shape notes, reused verbatim across every op that takes them,
    // so the wording never drifts between e.g. createOrUpdateVital's "citation"
    // and createOrUpdateNote's.
    const string CitationNote =
        "One SOUR citation: {\"source\": \"@S1@ or @NewSource1@\", \"page\"?, \"dataText\"?, \"quay\"? " +
        "(0-3)}. Accepts either the singular \"citation\" key with one object, or the plural " +
        "\"citations\" key with an array; omit both for no citation.";
    const string PersonRefNote =
        "An existing person as a bare xref string (\"@I5@\"), or as an inline description of a new " +
        "person to create: {\"xref\": \"@NewI1@\" (required — mints the real identity), \"name\", " +
        "\"sex\"? (\"M\"|\"F\"), \"facts\"? [{\"fact\", \"value\"?, \"citation\"/\"citations\"?}]}.";
    const string FactMatchNote =
        "Selects which same-tag fact instance this op targets when the record carries more than one: " +
        "a bare string (matched against the fact's date) or {\"text\"?, \"date\"?, \"place\"?}. Omit " +
        "when the tag occurs at most once, or to always target the sole/created instance.";
    const string EventValueNote =
        "A bare string for a text-payload fact (NAME), or {\"date\"?, \"place\"?} for a dated event. " +
        "At least one of date/place is expected for an event fact.";

    public static readonly IReadOnlyList<ChangesetOpDescriptor> Ops =
    [
        new("createOrUpdateVital", "createOrUpdate", "Vital",
            "Assert a dated/placed event (BIRT, DEAT, MARR, CENS, BURI, …) or a text fact (NAME) on a person " +
            "or family. Absent → create; identical → no-op; different → replaced unconditionally, old value " +
            "logged. A repeatable tag (anything but BIRT/DEAT/CHR/BAPM/BURI/CREM/SEX/NAME/MARR/DIV) refuses to " +
            "overwrite an existing instance without \"match\" or \"mode\":\"add\".",
            [
                new("record", "string", true, "Target person or family xref."),
                new("fact", "string", true, "The GEDCOM tag to assert, e.g. BIRT, DEAT, MARR, CENS, NAME."),
                new("value", "string | object", true, EventValueNote),
                new("match", "string | object", false, FactMatchNote),
                new("mode", "string", false, "\"upsert\" (default) or \"add\" (always creates a new instance; forbids \"match\")."),
                new("substructures", "array", false, "Extra GEDCOM substructures: [{\"tag\", \"value\"}]."),
                new("citation / citations", "object | array", false, CitationNote),
                new("replacedCitations", "string", false, "\"keep\" (default), \"drop\", or \"moveToNote\" — disposition of citations already on a fact whose value this op replaces."),
            ],
            Ex("""
                { "op": "createOrUpdateVital", "record": "@I2@", "fact": "DEAT",
                  "value": { "date": "19 JAN 1870", "place": "Salisbury, New Hampshire" },
                  "citation": { "source": "@NewSource1@", "page": "cert. 1870-0114", "quay": 2 } }
                """)),

        new("deleteVital", "delete", "Vital",
            "Remove a fact subtree. Absent → no-op.",
            [
                new("record", "string", true, "Target person or family xref."),
                new("fact", "string", true, "The GEDCOM tag to remove."),
                new("match", "string | object", false, FactMatchNote),
                new("deletedCitations", "string", false, "\"drop\" (default) or \"moveToNote\" (preserve the fact's citations under a NOTE on the record before removing the fact)."),
            ],
            Ex("""{ "op": "deleteVital", "record": "@I2@", "fact": "CENS", "match": { "date": "1850" } }""")),

        new("createOrUpdateSpouse", "createOrUpdate", "Spouse",
            "Ensure a family links two people as partners, with an optional marriage fact. The spouse may be " +
            "an existing person or described inline. \"family\" names an existing family to update, or a new " +
            "\"@NewF1@\" xref to create one; omit it only when the two people already share exactly one family.",
            [
                new("person", "string", true, "The already-known partner's xref."),
                new("spouse", "string | object", true, PersonRefNote),
                new("family", "string", false, "An existing family's xref, or a new \"@NewFn@\" xref to create one."),
                new("marriage", "object", false, "{\"date\"?, \"place\"?, \"citation\"/\"citations\"?} — the MARR fact, with its own citations nested inside this object, not at the op's top level."),
                new("note", "string", false, "A NOTE attached to the family record."),
                new("citation / citations", "object | array", false, CitationNote + " Attaches at the FAM record level, not to the marriage event."),
            ],
            Ex("""
                { "op": "createOrUpdateSpouse", "person": "@I1@",
                  "spouse": { "xref": "@NewI2@", "name": "Sarah /Blake/", "sex": "F" },
                  "family": "@NewF1@",
                  "marriage": { "date": "12 JUN 1865", "place": "Portland, Maine",
                                 "citation": { "source": "@S3@", "quay": 3 } } }
                """)),

        new("createOrUpdateChild", "createOrUpdate", "Child",
            "Ensure a person is a CHIL of a family. \"family\" is mandatory: an existing FAM is updated, or an " +
            "unused xref creates one, seeded by \"husb\"/\"wife\" (creation-only; use createOrUpdateSpouse to " +
            "change partners on an existing family).",
            [
                new("family", "string", true, "An existing family's xref, or a new \"@NewFn@\" xref to create one."),
                new("child", "string | object", true, PersonRefNote),
                new("husb", "string", false, "Father's xref — only when \"family\" creates a new family."),
                new("wife", "string", false, "Mother's xref — only when \"family\" creates a new family."),
                new("citation / citations", "object | array", false, CitationNote + " Attaches at the FAM record level."),
            ],
            Ex("""{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@I7@" }""")),

        new("createOrUpdateParent", "createOrUpdate", "Parent",
            "Ensure a person's parent family has the given parent in the given role. With one existing FAMC " +
            "the target family is implicit; with several, \"family\" is required.",
            [
                new("person", "string", true, "The child's xref."),
                new("role", "string", true, "\"father\" or \"mother\"."),
                new("parent", "string | object", true, PersonRefNote),
                new("family", "string", false, "Disambiguates which of the person's parent families to update, when there is more than one."),
                new("citation / citations", "object | array", false, CitationNote + " Attaches at the FAM record level."),
            ],
            Ex("""{ "op": "createOrUpdateParent", "person": "@I7@", "role": "father", "parent": "@I1@" }""")),

        new("createOrUpdateSource", "createOrUpdate", "Source",
            "Create or update a SOUR record. Creating one requires a new \"@NewSourceN@\" placeholder xref " +
            "and \"title\"; updating an existing xref only touches the fields supplied.",
            [
                new("xref", "string", true, "An existing source's xref, or a new \"@NewSourceN@\" placeholder to create one."),
                new("auth", "string", false, "Author."),
                new("title", "string", false, "Title — required when \"xref\" creates a new source."),
                new("url", "string", false, "Online location, folded into the source's composed NOTE."),
                new("accessed", "string", false, "Access date, folded into the source's composed NOTE."),
            ],
            Ex("""
                { "op": "createOrUpdateSource", "xref": "@NewSource1@",
                  "title": "Death certificate, Mercy (Whitfield) Ash, 1870",
                  "auth": "New Hampshire Bureau of Vital Records" }
                """)),

        new("deleteSource", "delete", "Source",
            "Remove a SOUR record. Refuses if any structure still cites it (remove those citations first). Absent → no-op.",
            [ new("xref", "string", true, "The source's xref.") ],
            Ex("""{ "op": "deleteSource", "xref": "@S9@" }""")),

        new("createOrUpdateCitation", "createOrUpdate", "Citation",
            "Attach one or more source citations to a fact that already exists (a citation cannot create its " +
            "fact — use createOrUpdateVital for that).",
            [
                new("record", "string", true, "The fact's person or family xref."),
                new("fact", "string", true, "The fact's GEDCOM tag."),
                new("match", "string | object", false, FactMatchNote),
                new("citation / citations", "object | array", true, CitationNote + " At least one is required."),
            ],
            Ex("""
                { "op": "createOrUpdateCitation", "record": "@I2@", "fact": "BIRT",
                  "citation": { "source": "@S1@", "page": "p. 12", "quay": 3 } }
                """)),

        new("deleteCitation", "delete", "Citation",
            "Remove one source's citation from a fact. Absent fact or absent citation → no-op.",
            [
                new("record", "string", true, "The fact's person or family xref."),
                new("fact", "string", true, "The fact's GEDCOM tag."),
                new("match", "string | object", false, FactMatchNote),
                new("source", "string", true, "The cited source's xref to remove."),
            ],
            Ex("""{ "op": "deleteCitation", "record": "@I2@", "fact": "BIRT", "source": "@S1@" }""")),

        new("createOrUpdateNote", "createOrUpdate", "Note",
            "Attach research reasoning or conflict analysis to a record (never a dated fact assertion — use " +
            "createOrUpdateVital for those). A note with the exact requested text → no-op; \"match\" naming " +
            "an existing note's exact text rewrites it; otherwise a new note is created.",
            [
                new("record", "string", true, "Target person or family xref."),
                new("text", "string", true, "The note's full text."),
                new("match", "string", false, "An existing note's exact current text, to rewrite instead of creating a new one."),
                new("mime", "string", false, "\"text/plain\" (default) or \"text/html\"."),
                new("citation / citations", "object | array", false, CitationNote + " Renders the note as a cited narrative paragraph rather than plain text."),
            ],
            Ex("""
                { "op": "createOrUpdateNote", "record": "@I4@",
                  "text": "The barque Corinth was lost with all hands in August 1842; Pratt (p. 131) is the only account naming Levi among the crew." }
                """)),

        new("deleteNote", "delete", "Note",
            "Remove a note, keyed by its exact full text. Absent → no-op.",
            [
                new("record", "string", true, "Target person or family xref."),
                new("text", "string", true, "The note's exact full text to remove."),
            ],
            Ex("""{ "op": "deleteNote", "record": "@I4@", "text": "Superseded by the 1870 death certificate." }""")),

        new("createOrUpdateMedia", "createOrUpdate", "Media",
            "Create or update an OBJE record (one or more files) and optionally attach it to people/families. " +
            "Keyed by \"xref\" when given, otherwise by the exact set of \"files\" paths.",
            [
                new("xref", "string", false, "An existing media object's xref, or a new \"@NewMn@\" placeholder to create one; omit to key by \"files\" instead."),
                new("title", "string", false, "The OBJE record's own title."),
                new("files", "array", true, "At least one {\"path\", \"mediaType\" (e.g. \"image/jpeg\"), \"medium\"?, \"title\"?}. A differing list on an existing object replaces it wholesale."),
                new("attachTo", "array", false, "[{\"person\" xref XOR \"family\" xref, \"title\"?, \"portrait\"? (bool — moves this link first, the generator's rendered-portrait slot)}]."),
            ],
            Ex("""
                { "op": "createOrUpdateMedia",
                  "files": [ { "path": "photos/mercy-ash.jpg", "mediaType": "image/jpeg", "title": "Portrait, c. 1855" } ],
                  "attachTo": [ { "person": "@I2@", "portrait": true } ] }
                """)),

        new("deleteMedia", "delete", "Media",
            "Remove an OBJE record and every link to it across the file. Unlike the other delete ops, an " +
            "absent xref fails validation rather than being a no-op.",
            [ new("xref", "string", true, "The media object's xref.") ],
            Ex("""{ "op": "deleteMedia", "xref": "@M3@" }""")),

        new("deleteSpouse", "delete", "Spouse",
            "Remove one partner's link (and back-link) from the family the two people share. A family left " +
            "with no partners or children is deleted. No shared family, or not a partner → no-op.",
            [
                new("person", "string", true, "One partner's xref."),
                new("spouse", "string", true, "The other partner's xref to unlink."),
                new("family", "string", false, "Disambiguates which shared family, when the two people share more than one."),
            ],
            Ex("""{ "op": "deleteSpouse", "person": "@I1@", "spouse": "@I2@" }""")),

        new("deleteChild", "delete", "Child",
            "Remove a CHIL link (and the child's back-link). A family left with no partners or children is deleted. Absent → no-op.",
            [
                new("family", "string", true, "The family's xref."),
                new("child", "string", true, "The child's xref to unlink."),
            ],
            Ex("""{ "op": "deleteChild", "family": "@F1@", "child": "@I7@" }""")),

        new("deleteParent", "delete", "Parent",
            "Remove the father/mother link (and back-link) from a person's parent family. Absent → no-op.",
            [
                new("person", "string", true, "The child's xref."),
                new("role", "string", true, "\"father\" or \"mother\"."),
                new("family", "string", false, "Disambiguates which parent family, when the person has more than one."),
            ],
            Ex("""{ "op": "deleteParent", "person": "@I7@", "role": "father" }""")),

        new("mergePerson", "merge", "Person",
            "Fold a duplicate INDI into a survivor. Facts present on only one side are unioned onto the " +
            "survivor automatically; a fact tag present on both sides with a differing value is a conflict " +
            "that must be resolved explicitly via \"facts\" (this op never guesses which side is correct). " +
            "Every FAMS/FAMC the duplicate held, and every pointer elsewhere in the file naming it, is " +
            "redirected to the survivor; the duplicate record is then deleted.",
            [
                new("survivor", "string", true, "The xref to keep."),
                new("duplicate", "string", true, "The xref to fold in and delete."),
                new("facts", "array", false, "Conflict resolutions: [{\"fact\" (tag), \"keep\"? (\"survivor\"|\"duplicate\"|\"value\", default \"value\"), \"value\"? (required when \"keep\" is \"value\"), \"citation\"/\"citations\"?}]."),
                new("note", "string", false, "The merge-audit NOTE text left on the survivor; defaults to \"Merged duplicate {duplicate} into this record.\""),
            ],
            Ex("""
                { "op": "mergePerson", "survivor": "@I2@", "duplicate": "@I9@",
                  "facts": [ { "fact": "BIRT", "keep": "value", "value": { "date": "24 SEP 1930", "place": "Salisbury, New Hampshire" } } ] }
                """)),
    ];
}

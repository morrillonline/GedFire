using System.Text.Json;
using GedCore.Contract;

namespace GedFire.Mcp;

/// <summary>One field of a changeset object, as reported by describe_changeset_ops.</summary>
public sealed record ChangesetOpField(string Name, string Type, bool Required, string Description);

/// <summary>One changeset op kind's shape: its fields and one worked example.</summary>
public sealed record ChangesetOpDescriptor(
    string Op, string Verb, string Noun, string Summary,
    IReadOnlyList<ChangesetOpField> Fields, JsonElement Example);

/// <summary>One structure an op or the envelope holds (a citation, a people entry…), with its fields.</summary>
public sealed record ChangesetStructure(string Name, string Description, IReadOnlyList<ChangesetOpField> Fields);

/// <summary>The changeset file's own envelope (proposal/newSources/items/findings), independent of any op.</summary>
public sealed record ChangesetEnvelope(string Description, JsonElement Example);

public sealed record DescribeChangesetOpsResult(
    ChangesetEnvelope Envelope, IReadOnlyList<ChangesetStructure> Structures, IReadOnlyList<ChangesetOpDescriptor> Ops);

// ---------------------------------------------------------------------------
// Reference data for describe_changeset_ops: the readable guide to the
// changeset contract. Every field, type and requirement is read from
// ChangesetDialect, the table the contract checker and the published
// changeset schema are built from, so the guide cannot drift from either;
// only each op's summary and worked example are written here.
// ---------------------------------------------------------------------------

public static class ChangesetOpsCatalog
{
    static JsonElement Ex(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static readonly ChangesetEnvelope Envelope = new(
        "The whole changeset file's shape, independent of any one op (structure \"changeset\"). Every object " +
        "takes only the fields listed for it. \"proposal\" is an optional free-text label. \"newSources\" groups are " +
        "optional: each names the xref its ops define; a group is applied unconditionally unless some item in the " +
        "changeset cites its xref, in which case it applies only when a selected item (per the \"items\" argument) " +
        "cites it -- so excluding an item also excludes a source only that item needed. \"items\" is the numbered list " +
        "validate_changeset/apply_changeset's own \"items\" argument selects from (\"all\", or e.g. \"1,3\"); item " +
        "numbers need not be contiguous or sorted, and \"target\" is a free-text label for a human reviewer, not read " +
        "by either tool. An item's \"people\" declares, once, each new person its ops create; a spouse, child or parent " +
        "field names a person only by xref. \"citations\" is always an array and cites the fact or note the op writes; " +
        "a relationship op cites the family record in \"familyCitations\". A fact takes \"value\" (a string) or " +
        "\"date\" and \"place\" as its tag fixes: NAME and SEX take value, an event such as BIRT takes date and place, " +
        "        an attribute such as OCCU or RESI takes all three. Redraft a changeset written before the contract from its " +
                "finding with draft_changeset.",
        Ex($$"""
            {
              "proposal": "mercy-whitfield-death-record",
              "newSources": [
                { "xref": "@NewSource1@", "ops": [
                  { "op": "createOrUpdateSource", "xref": "@NewSource1@",
                    "title": "Death certificate, Mercy (Whitfield) Ash, 1870",
                    "auth": "New Hampshire Bureau of Vital Records" } ] }
              ],
              "items": [
                { "item": 1, "target": "@I2@ Mercy Whitfield -> cite her death and add her husband",
                  "people": [ { "xref": "@NewI1@", "name": "Nathaniel /Ash/", "sex": "M" } ],
                  "ops": [
                    { "op": "createOrUpdateVital", "record": "@I2@", "fact": "DEAT",
                      "date": "19 JAN 1870", "place": "Salisbury, New Hampshire",
                      "citations": [ { "source": "@NewSource1@", "page": "cert. 1870-0114",
                                       "dataText": "Mercy Ash, widow of Nathaniel, died 19 Jan 1870, aged 75",
                                       "quay": 2 } ] },
                    { "op": "createOrUpdateSpouse", "person": "@I2@", "spouse": "@NewI1@", "family": "@NewF1@",
                      "familyCitations": [ { "source": "@NewSource1@", "page": "cert. 1870-0114", "quay": 2 } ] } ] }
              ]
            }
            """));

    sealed record OpGuide(string Op, string Verb, string Noun, string Summary, string Example);

    static readonly OpGuide[] Guides =
    [
        new("createOrUpdateVital", "createOrUpdate", "Vital",
            "Assert a fact on a person or family: an event (BIRT, DEAT, MARR, CENS, BURI, …) by date and place, a text " +
            "fact (NAME, SEX) by value, or an attribute (OCCU, RESI, EVEN, …) by value, date and place. Absent → create; " +
            "identical → no-op; different → replaced unconditionally, old value logged. A repeatable tag (anything but " +
            "BIRT/DEAT/CHR/BAPM/BURI/CREM/SEX/NAME/MARR/DIV) refuses to overwrite an existing instance without \"match\" or " +
            "\"mode\":\"add\". A family event (" + string.Join("/", GedCore.FamilyEvents.Tags.Order(StringComparer.Ordinal)) +
            ") is refused on a person; record it on the couple's family. draft_changeset writes only these tags: " + string.Join(", ", GedCore.Apply.CreateOrUpdateVitalOp.Facts) + ".",
            """
            { "op": "createOrUpdateVital", "record": "@I2@", "fact": "DEAT",
              "date": "19 JAN 1870", "place": "Salisbury, New Hampshire",
              "citations": [ { "source": "@S1@", "page": "cert. 1870-0114", "quay": 2 } ] }
            """),
        new("deleteVital", "delete", "Vital",
            "Remove a fact subtree. Absent → no-op.",
            """{ "op": "deleteVital", "record": "@I2@", "fact": "CENS", "match": { "date": "1850" } }"""),
        new("createOrUpdateSpouse", "createOrUpdate", "Spouse",
            "Ensure a family links two people as partners, with an optional marriage fact. The spouse is an existing " +
            "person, or a new one declared in the item's people. \"family\" names an existing family to update, or a new " +
            "\"@NewF1@\" xref to create one; omit it only when the two people already share exactly one family. " +
            "\"familyCitations\" cites the family record; the marriage's own citations sit inside \"marriage\".",
            """
            { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@I3@", "family": "@NewF1@",
              "marriage": { "date": "12 JUN 1865", "place": "Portland, Maine",
                            "citations": [ { "source": "@S3@", "quay": 3 } ] } }
            """),
        new("createOrUpdateChild", "createOrUpdate", "Child",
            "Ensure a person is a CHIL of a family. \"family\" is mandatory: an existing FAM is updated, or an unused xref " +
            "creates one, seeded by \"husb\"/\"wife\" (creation-only; use createOrUpdateSpouse to change partners on an " +
            "existing family). \"familyCitations\" cites the family record.",
            """{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@I7@" }"""),
        new("createOrUpdateParent", "createOrUpdate", "Parent",
            "Ensure a person's parent family has the given parent in the given role. With one existing FAMC the target " +
            "family is implicit; with several, \"family\" is required. \"familyCitations\" cites the family record.",
            """{ "op": "createOrUpdateParent", "person": "@I7@", "role": "father", "parent": "@I1@" }"""),
        new("createOrUpdateSource", "createOrUpdate", "Source",
            "Create or update a SOUR record. Creating one requires a new \"@NewSourceN@\" placeholder xref and \"title\"; if " +
            "the file already has a source with the same title and author (and no publication), that source is used and " +
            "nothing is created or changed. Updating an existing xref only touches the fields supplied — unless more than " +
            "one fact cites the source: then the update is applied to a copy (or to an existing source that is exactly the " +
            "updated one) and only the facts this item cites it on point to it; the original and every other citation of " +
            "it are untouched. An item that updates such a shared source must also cite it on the fact that needs the change.",
            """
            { "op": "createOrUpdateSource", "xref": "@NewSource1@",
              "title": "Death certificate, Mercy (Whitfield) Ash, 1870",
              "auth": "New Hampshire Bureau of Vital Records" }
            """),
        new("deleteSource", "delete", "Source",
            "Remove a SOUR record. Refuses if any structure still cites it (remove those citations first). Absent → no-op.",
            """{ "op": "deleteSource", "xref": "@S9@" }"""),
        new("createOrUpdateCitation", "createOrUpdate", "Citation",
            "Attach one or more source citations to a fact that already exists (a citation cannot create its fact — use " +
            "createOrUpdateVital for that). State each citation as it should read; nothing is removed or re-added by hand. " +
            "A source already cited once on the fact is updated in place, so stating a new \"page\" corrects the page. To " +
            "cite one source on several pages of the same fact, give one entry per page; an entry whose page matches an " +
            "existing citation updates it, and an entry that matches none is added. If the fact already cites the source " +
            "several times and the entries stated cannot be told apart from them, the op is rejected and names the existing " +
            "pages — state every citation of that source you want. Name a \"note\" (its exact text) to address the " +
            "citations under one of the record's notes instead of a fact. Omit both \"fact\" and \"note\" to address a " +
            "family record's own citations (the relationship provenance written by createOrUpdateSpouse/Child/Parent); " +
            "that form works on family records only.",
            """
            { "op": "createOrUpdateCitation", "record": "@I2@", "fact": "BIRT",
              "citations": [ { "source": "@S1@", "page": "p. 12", "quay": 3 } ] }
            """),
        new("deleteCitation", "delete", "Citation",
            "Remove one source's citation from a fact, from a note (named by its exact text via \"note\"), or — with " +
            "neither named, on a family record — from the family's own citations. Absent structure or absent citation → no-op.",
            """{ "op": "deleteCitation", "record": "@I2@", "fact": "BIRT", "source": "@S1@" }"""),
        new("createOrUpdateNote", "createOrUpdate", "Note",
            "Attach research reasoning or conflict analysis to a record (never a dated fact assertion — use " +
            "createOrUpdateVital for those). A note with the exact requested text → no-op; \"match\" naming an existing " +
            "note's exact text rewrites it; otherwise a new note is created. With \"purpose\": \"biography\" the note is a " +
            "person's cited biographical prose (service, occupation, migration, probate…), written as a plain note with its " +
            "citations: a citation it gains that none of the person's cited notes carried before (same source and page), or " +
            "text that matches none of the person's notes (case, whitespace and punctuation folded), " +
            "earns biography credit. Citations render the note as a cited narrative paragraph.",
            """
            { "op": "createOrUpdateNote", "record": "@I4@",
              "text": "The barque Corinth was lost with all hands in August 1842; Pratt (p. 131) is the only account naming Levi among the crew." }
            """),
        new("deleteNote", "delete", "Note",
            "Remove a note, keyed by its exact full text. Absent → no-op.",
            """{ "op": "deleteNote", "record": "@I4@", "text": "Superseded by the 1870 death certificate." }"""),
        new("createOrUpdateMedia", "createOrUpdate", "Media",
            "Create or update an OBJE record (one or more files) and optionally attach it to people/families. Keyed by " +
            "\"xref\" when given, otherwise by the exact set of \"files\" paths.",
            """
            { "op": "createOrUpdateMedia",
              "files": [ { "path": "photos/mercy-ash.jpg", "mediaType": "image/jpeg", "title": "Portrait, c. 1855" } ],
              "attachTo": [ { "person": "@I2@", "portrait": true } ] }
            """),
        new("deleteMedia", "delete", "Media",
            "Remove an OBJE record and every link to it across the file. Unlike the other delete ops, an absent xref fails " +
            "validation rather than being a no-op.",
            """{ "op": "deleteMedia", "xref": "@M3@" }"""),
        new("deleteSpouse", "delete", "Spouse",
            "Remove one partner's link (and back-link) from the family the two people share. A family left with no " +
            "partners or children is deleted. No shared family, or not a partner → no-op.",
            """{ "op": "deleteSpouse", "person": "@I1@", "spouse": "@I2@" }"""),
        new("deleteChild", "delete", "Child",
            "Remove a CHIL link (and the child's back-link). A family left with no partners or children is deleted. Absent → no-op.",
            """{ "op": "deleteChild", "family": "@F1@", "child": "@I7@" }"""),
        new("deleteParent", "delete", "Parent",
            "Remove the father/mother link (and back-link) from a person's parent family. Absent → no-op.",
            """{ "op": "deleteParent", "person": "@I7@", "role": "father" }"""),
        new("mergePerson", "merge", "Person",
            "Fold a duplicate INDI into a survivor. Facts present on only one side are unioned onto the survivor " +
            "automatically; a fact tag present on both sides with a differing value is a conflict that must be resolved " +
            "explicitly via \"facts\" (this op never guesses which side is correct). Every FAMS/FAMC the duplicate held, " +
            "and every pointer elsewhere in the file naming it, is redirected to the survivor; the duplicate record is then " +
            "deleted. The default merge-audit note reads \"Merged duplicate record of {the duplicate's name} into this record.\"",
            """
            { "op": "mergePerson", "survivor": "@I2@", "duplicate": "@I9@",
              "facts": [ { "fact": "BIRT", "keep": "value", "date": "24 SEP 1930", "place": "Salisbury, New Hampshire" } ] }
            """),
    ];

    public static readonly IReadOnlyList<ChangesetOpDescriptor> Ops =
        [.. Guides.Select(g => new ChangesetOpDescriptor(
            g.Op, g.Verb, g.Noun, g.Summary, FieldsOf(ChangesetDialect.FindOp(g.Op)!), Ex(g.Example)))];

    public static readonly IReadOnlyList<ChangesetStructure> Structures =
        [.. ChangesetDialect.Structures.Select(s => new ChangesetStructure(s.Name, s.Description, FieldsOf(s)))];

    // The op field names the op itself; every other field is listed as the dialect defines it.
    static IReadOnlyList<ChangesetOpField> FieldsOf(DialectShape shape) =>
        [.. shape.Fields.Where(f => f.Name != ChangesetDialect.Op)
            .Select(f => new ChangesetOpField(f.Name, TypeOf(f), f.Required, f.Description))];

    static string TypeOf(DialectField field)
    {
        string type = field.Type switch
        {
            DialectType.String => "string",
            DialectType.Integer => "integer",
            DialectType.Boolean => "boolean",
            DialectType.StringArray => "array of string",
            DialectType.Object => $"object ({field.Shape})",
            _ => field.Shape == ChangesetDialect.Op ? "array of op" : $"array of {field.Shape}",
        };
        return field.Values.Count > 1 ? $"{type}: {string.Join(" | ", field.Values.Select(v => $"\"{v}\""))}" : type;
    }
}

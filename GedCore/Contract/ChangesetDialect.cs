namespace GedCore.Contract;

/// <summary>The JSON type of one changeset field.</summary>
public enum DialectType { String, Integer, Boolean, Object, StringArray, ObjectArray }

/// <summary>One field of a changeset object. An object or object array names the shape it holds.</summary>
public sealed record DialectField(string Name, DialectType Type, string Description)
{
    public bool Required { get; init; }

    /// <summary>The shape an object, or each element of an object array, holds; <see cref="ChangesetDialect.Op"/> for an op.</summary>
    public string? Shape { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];
    public string? Pattern { get; init; }
    public int? Minimum { get; init; }
    public int? Maximum { get; init; }
    public int? MinItems { get; init; }
}

/// <summary>One object of the changeset dialect: an op, or a structure an op or the envelope holds.</summary>
public sealed record DialectShape(string Name, string Description, IReadOnlyList<DialectField> Fields)
{
    public DialectField? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
}

/// <summary>
/// The changeset contract: every object a changeset holds and every field each object takes. The contract
/// checker, the published JSON Schema and describe_changeset_ops are all read from here, so they cannot drift.
/// </summary>
public static class ChangesetDialect
{
    public const string SchemaVersion = "1.1";
    public const int SupportedMajor = 1;

    /// <summary>The shape name an op array names: each element is the op its "op" field names.</summary>
    public const string Op = "op";

    public const string Envelope = "changeset";

    const string XrefPattern = "^@[^@]+@$";

    static DialectField Str(string name, string description, bool required = false) =>
        new(name, DialectType.String, description) { Required = required };

    static DialectField Xref(string name, string description, bool required = false) =>
        new(name, DialectType.String, description) { Required = required, Pattern = XrefPattern };

    static DialectField Enum(string name, string description, params string[] values) =>
        new(name, DialectType.String, description) { Values = values };

    static DialectField Obj(string name, string shape, string description, bool required = false) =>
        new(name, DialectType.Object, description) { Shape = shape, Required = required };

    static DialectField Objs(string name, string shape, string description, bool required = false, int? minItems = null) =>
        new(name, DialectType.ObjectArray, description) { Shape = shape, Required = required, MinItems = minItems };

    static DialectField Strs(string name, string description) => new(name, DialectType.StringArray, description);

    static DialectField Citations(string description) =>
        Objs("citations", "citation", "Source citations, always an array; omit or leave empty for none. " + description);

    static readonly DialectField Findings =
        Strs("findings", "The ids of the findings-table entries this op rests on, written by draft_changeset.");

    static readonly DialectField Value = Str("value",
        "The fact's text payload, a string: the name for NAME, the sex for SEX, the description for OCCU, RESI, EVEN and the other attributes. An event such as BIRT takes date and place instead.");

    static readonly DialectField Date = Str("date",
        "A GEDCOM 7 date, for an event or a dated attribute. A text fact such as NAME takes no date.");

    static readonly DialectField Place = Str("place",
        "The place, for an event or a placed attribute. A text fact such as NAME takes no place.");

    static readonly DialectField Match = Obj("match", "factMatch",
        "Selects which same-tag fact instance this op targets when the record carries more than one. Omit when the tag occurs at most once, or to always target the sole or created instance.");

    static readonly DialectField NotDuplicateOf = Strs("notDuplicateOf",
        "Existing person xrefs already confirmed not to be the same person as a new person this op creates; overrides the high-confidence duplicate rejection, and the refusal of a new relative the tree person already has (a parent in the same role, or a spouse or child of the same name), for those xrefs only.");

    static readonly DialectField FamilyCitations = Objs("familyCitations", "citation",
        "Citations of the family record itself (the relationship's provenance), always an array. GEDCOM 7 allows a citation on FAM but not under HUSB, WIFE or CHIL.");

    static DialectField PersonRef(string name, string description) => Xref(name,
        description + " An existing person's xref, or a @New…@ placeholder declared in the item's people.", required: true);

    static DialectShape OpShape(string op, params DialectField[] fields) =>
        new(op, $"The {op} op.", [new(Op, DialectType.String, "The op's kind.") { Required = true, Values = [op] }, .. fields, Findings]);

    public static readonly IReadOnlyList<DialectShape> Ops =
    [
        OpShape("createOrUpdateVital",
            Xref("record", "Target person or family xref.", required: true),
            Str("fact", "The GEDCOM tag to assert. NAME and SEX take value; BIRT, DEAT, MARR, CENS and the other events take date and place; OCCU, RESI, EVEN and the other attributes take value, date and place.", required: true),
            Value, Date, Place, Match,
            Enum("mode", "\"upsert\" (default) updates a matched or sole instance or creates one; \"add\" creates a new instance unless one with the same value, date and place exists, which gains the citations instead. \"add\" forbids match.", "upsert", "add"),
            Objs("substructures", "substructure", "Extra GEDCOM substructures written under the fact."),
            Citations("They cite the fact this op writes."),
            Enum("replacedCitations", "What happens to the citations already on a fact whose value this op replaces: \"keep\" (default), \"drop\", or \"moveToNote\".", "keep", "drop", "moveToNote")),
        OpShape("deleteVital",
            Xref("record", "Target person or family xref.", required: true),
            Str("fact", "The GEDCOM tag to remove.", required: true),
            Match,
            Enum("deletedCitations", "\"drop\" (default) or \"moveToNote\", which keeps the fact's citations under a NOTE on the record.", "drop", "moveToNote")),
        OpShape("createOrUpdateSpouse",
            Xref("person", "The already-known partner's xref.", required: true),
            PersonRef("spouse", "The other partner."),
            Xref("family", "An existing family's xref, or a new @NewF…@ placeholder to create one; omit only when the two already share exactly one family."),
            Obj("marriage", "marriage", "The MARR fact, with its own citations inside it."),
            Str("note", "A NOTE attached to the family record."),
            FamilyCitations, NotDuplicateOf),
        OpShape("createOrUpdateChild",
            Xref("family", "An existing family's xref, or a new @NewF…@ placeholder to create one.", required: true),
            PersonRef("child", "The child."),
            Xref("husb", "The father's xref, only when family creates a new family."),
            Xref("wife", "The mother's xref, only when family creates a new family."),
            FamilyCitations, NotDuplicateOf),
        OpShape("createOrUpdateParent",
            Xref("person", "The child's xref.", required: true),
            Enum("role", "\"father\" or \"mother\".", "father", "mother") with { Required = true },
            PersonRef("parent", "The parent."),
            Xref("family", "Which of the person's parent families to update, when there is more than one; a new @NewF…@ placeholder creates one."),
            FamilyCitations, NotDuplicateOf),
        OpShape("createOrUpdateSource",
            Xref("xref", "An existing source's xref, or a new @NewSource…@ placeholder to create one.", required: true),
            Str("auth", "Author."),
            Str("title", "Title; required when xref creates a new source."),
            Str("url", "Online location, folded into the source's composed NOTE."),
            Str("accessed", "Access date, folded into the source's composed NOTE.")),
        OpShape("deleteSource",
            Xref("xref", "The source's xref.", required: true)),
        OpShape("createOrUpdateCitation",
            Xref("record", "The fact's person or family xref, or the family whose own citations are addressed.", required: true),
            Str("fact", "The fact's GEDCOM tag. Omit it and note to address a family's own citations (family records only)."),
            Str("note", "The exact current text of one of the record's notes, to address that note's citations instead of a fact's."),
            Match,
            Citations("At least one is required.") with { Required = true, MinItems = 1 }),
        OpShape("deleteCitation",
            Xref("record", "The fact's person or family xref, or the family whose own citations are addressed.", required: true),
            Str("fact", "The fact's GEDCOM tag. Omit it and note to address a family's own citations (family records only)."),
            Str("note", "The exact current text of one of the record's notes, to address that note's citations instead of a fact's."),
            Match,
            Xref("source", "The cited source's xref to remove.", required: true),
            Str("page", "Which citation to remove when the fact cites the source on several pages; required only then.")),
        OpShape("createOrUpdateNote",
            Xref("record", "Target person or family xref; a person for purpose biography.", required: true),
            Str("text", "The note's full text. Refer to people and sources by name; text containing a record id such as @S1@ is refused.", required: true),
            Str("match", "An existing note's exact current text, to rewrite instead of creating a new one."),
            Str("mime", "\"text/plain\" (default) or \"text/html\"."),
            Str("purpose", "\"biography\" to claim biography credit for the citations this note adds. Read from the changeset only; nothing extra is written to the file."),
            Citations("They cite the note.")),
        OpShape("deleteNote",
            Xref("record", "Target person or family xref.", required: true),
            Str("text", "The note's exact full text to remove.", required: true)),
        OpShape("createOrUpdateMedia",
            Xref("xref", "An existing media object's xref, or a new @NewM…@ placeholder to create one; omit to key by files instead."),
            Str("title", "The OBJE record's own title."),
            Objs("files", "mediaFile", "At least one file. A differing list on an existing object replaces it wholesale.", required: true, minItems: 1),
            Objs("attachTo", "mediaLink", "The people and families to link the object to.")),
        OpShape("deleteMedia",
            Xref("xref", "The media object's xref.", required: true)),
        OpShape("deleteSpouse",
            Xref("person", "One partner's xref.", required: true),
            Xref("spouse", "The other partner's xref to unlink.", required: true),
            Xref("family", "Which shared family, when the two share more than one.")),
        OpShape("deleteChild",
            Xref("family", "The family's xref.", required: true),
            Xref("child", "The child's xref to unlink.", required: true)),
        OpShape("deleteParent",
            Xref("person", "The child's xref.", required: true),
            Enum("role", "\"father\" or \"mother\".", "father", "mother") with { Required = true },
            Xref("family", "Which parent family, when the person has more than one.")),
        OpShape("mergePerson",
            Xref("survivor", "The xref to keep.", required: true),
            Xref("duplicate", "The xref to fold in and delete.", required: true),
            Objs("facts", "mergeFact", "How each conflicting fact is resolved."),
            Str("note", "The merge-audit NOTE text left on the survivor. Must not contain a record id.")),
    ];

    public static readonly IReadOnlyList<DialectShape> Structures =
    [
        new(Envelope, "A changeset: numbered items of ops, the new sources they cite, and the findings they rest on.",
        [
            new("schemaVersion", DialectType.String, $"The changeset contract version the file is written to, \"{SchemaVersion}\". A major version GedFire does not read is refused.") { Pattern = @"^\d+\.\d+$" },
            Str("proposal", "A free-text label for the proposal."),
            Objs("newSources", "sourceGroup", "Sources the items cite, each group keyed by the xref its ops define. A group some item cites is applied only when a selected item cites it."),
            Objs("items", "item", "The numbered items validate_changeset and apply_changeset select from.", required: true),
            Objs("findings", "finding", "The findings table draft_changeset writes: the evidence each op's findings names."),
        ]),
        new("sourceGroup", "One newSources entry.",
        [
            Xref("xref", "The source xref the group's ops define.", required: true),
            Objs("ops", Op, "The ops that write the source.", required: true),
        ]),
        new("item", "One numbered item.",
        [
            new("item", DialectType.Integer, "The item's number, which the items argument selects.") { Required = true },
            Str("target", "A free-text label for a human reviewer; never read."),
            Objs("people", "person", "The new people this item's ops create, each declared once and named by its placeholder xref in a spouse, child or parent field."),
            Objs("ops", Op, "The item's ops, applied in order.", required: true),
        ]),
        new("person", "A new person an item creates.",
        [
            Xref("xref", "A @New…@ placeholder; apply mints the real xref.", required: true),
            Str("name", "The person's NAME, e.g. \"Sarah /Blake/\".", required: true),
            Enum("sex", "The person's SEX.", "M", "F", "U", "X"),
            Objs("facts", "personFact", "Facts written on the new person when it is created."),
        ]),
        new("personFact", "A fact written on a new person.",
        [
            Str("fact", "The GEDCOM tag.", required: true),
            Value, Date, Place,
            Citations("They cite this fact."),
        ]),
        new("citation", "One source citation.",
        [
            Xref("source", "The cited source: an existing xref, or a @NewSource…@ placeholder a newSources group defines.", required: true),
            Str("page", "Where in the source: the locator, copied verbatim."),
            Str("dataText", "The verbatim extract."),
            new("quay", DialectType.Integer, "The GEDCOM QUAY assessment, 0 to 3.") { Minimum = 0, Maximum = 3 },
        ]),
        new("marriage", "The MARR fact a createOrUpdateSpouse writes on the family.",
        [
            Date, Place,
            Citations("They cite the MARR fact. At least one is required.") with { Required = true, MinItems = 1 },
        ]),
        new("factMatch", "A fact selector: the instance whose fields equal every field given.",
        [
            Str("value", "The instance's text payload, for a text fact or an attribute."),
            Str("date", "The instance's date, for an event or a dated attribute."),
            Str("place", "The instance's place, for an event or a placed attribute."),
        ]),
        new("substructure", "A GEDCOM substructure under a fact.",
        [
            Str("tag", "The substructure's tag, e.g. TYPE.", required: true),
            Str("value", "Its payload.", required: true),
        ]),
        new("mediaFile", "One file of a media object.",
        [
            Str("path", "The file's path; media/ is prefixed to a relative path that lacks it.", required: true),
            Str("mediaType", "The MIME type, e.g. image/jpeg.", required: true),
            Str("medium", "The GEDCOM MEDI value, e.g. photo."),
            Str("title", "The file's own title."),
        ]),
        new("mediaLink", "One person or family a media object is linked to: give person or family.",
        [
            Xref("person", "A person's xref."),
            Xref("family", "A family's xref."),
            Str("title", "The link's title, overriding the object's."),
            new("portrait", DialectType.Boolean, "Move this link first, the generator's portrait slot."),
        ]),
        new("mergeFact", "How one conflicting fact is resolved in a merge.",
        [
            Str("fact", "The GEDCOM tag.", required: true),
            Enum("keep", "\"survivor\", \"duplicate\", or \"value\" (default), which writes value, date and place.", "survivor", "duplicate", "value"),
            Value, Date, Place,
            Citations("They cite the fact that survives."),
        ]),
        new("finding", "One findings-table entry: the evidence a reviewer weighs.",
        [
            Str("id", "The finding's id, which ops name in their findings.", required: true),
            new("item", DialectType.Integer, "The item the finding was drafted into."),
            Str("extract", "The verbatim extract."),
            Obj("source", "findingSource", "The source the extract is from."),
            Objs("claims", "claim", "What the finding asserts, each graded."),
            Obj("argument", "argument", "The finding's identity argument, carried unchanged."),
        ]),
        new("findingSource", "The source of a finding.",
        [
            Str("ref", "The source xref or placeholder the citations name."),
            Str("name", "The collection."),
            Str("author", "The author."),
            Str("url", "The online location."),
            Str("locator", "The locator, copied into each citation's page."),
            Str("accessed", "The access date."),
            Str("class", "The source class."),
            Str("information", "The information class."),
        ]),
        new("claim", "One graded claim of a finding: a fact, or a relation.",
        [
            Str("record", "The fact's record."),
            Str("fact", "The fact's tag."),
            Str("date", "The fact's date."),
            Str("place", "The fact's place."),
            Str("value", "The fact's text value."),
            new("allowSurnameChange", DialectType.Boolean, "The finding allowed a surname change."),
            new("quay", DialectType.Integer, "The claim's QUAY.") { Minimum = 0, Maximum = 3 },
            Str("class", "The source class, when the claim overrides it."),
            Str("information", "The information class, when the claim overrides it."),
            Str("replaceHeld", "Why the fact replaces the record's value."),
            Str("withdraw", "Why the fact is withdrawn."),
            Str("relation", "The relation kind: spouse, child or parent."),
            Str("person", "The relation's person; empty for a child relation."),
            Str("other", "The relation's other person."),
            Str("family", "The relation's family."),
            Str("role", "The relation's role."),
        ]),
        new("argument", "A finding's identity argument.",
        [
            Strs("anchors", "Why him: the anchoring facts."),
            Str("identity", "How the identity was resolved."),
            Objs("exclusions", "exclusion", "One entry per excluded rival."),
            Str("cautions", "Conflicts weighed."),
            Enum("difficulty", "How hard the identity was.", "low", "medium", "high"),
        ]),
        new("exclusion", "One excluded rival.",
        [
            Str("name", "The rival's name."),
            Str("xref", "The rival's xref."),
            Str("fact", "The fact that excludes the rival."),
            Str("finding", "The finding that shows it."),
        ]),
    ];

    static readonly Dictionary<string, DialectShape> ByName =
        Ops.Concat(Structures).ToDictionary(s => s.Name, StringComparer.Ordinal);

    public static DialectShape Shape(string name) => ByName[name];

    public static DialectShape? FindOp(string? op) => op is null ? null : Ops.FirstOrDefault(s => s.Name == op);
}

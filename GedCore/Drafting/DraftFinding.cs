using System.Text.Json.Nodes;

namespace GedCore.Drafting;

/// <summary>
/// Where a finding came from. <see cref="Name"/> is the collection (one source record is
/// minted per collection and author); <see cref="Locator"/> becomes each citation's page verbatim.
/// </summary>
public sealed record DraftSource(
    string Name, string? Author, string? Url, string Locator, string? Accessed, string Class, string Information);

/// <summary>
/// One fact the author asserts from a finding: the value is the author's judgement, the grade is
/// per fact, and <see cref="Excerpt"/> narrows the citation's extract when the whole finding's would over-quote.
/// <see cref="Class"/> and <see cref="Information"/> override the source's when this fact differs.
/// A text fact (a NAME) carries its <see cref="Value"/> instead of a date and place. A <see cref="CiteOnly"/> fact
/// only adds this finding's citation to a fact the record already holds; its date and place then select the instance.
/// </summary>
public sealed record DraftFact(
    string Record, string Tag, string? Date, string? Place, int Quay,
    string? Class, string? Information, string? Excerpt, string? Value = null)
{
    public IReadOnlyList<DraftSubstructure> Substructures { get; init; } = [];
    public string? ReplacedCitations { get; init; }
    public bool CiteOnly { get; init; }
}

/// <summary>An extra line under a fact, such as the TYPE of an EVEN.</summary>
public sealed record DraftSubstructure(string Tag, string Value);

/// <summary>
/// A note the author wrote. <see cref="Match"/> is the exact text of an existing note to rewrite;
/// <see cref="Cites"/> attach citations of findings in the same draft.
/// </summary>
public sealed record DraftNote(string Record, string Text)
{
    public string? Match { get; init; }
    public IReadOnlyList<DraftNoteCitation> Cites { get; init; } = [];
}

/// <summary>A citation on a note: the finding it cites (the note's own when null), graded on its own, optionally with a narrower extract.</summary>
public sealed record DraftNoteCitation(string? Finding, int Quay, string? Excerpt);

public sealed record DraftTarget(string Xref, string? Card, string? Name);

/// <summary>A person the changeset creates, named by a placeholder xref such as "@NewI1@".</summary>
public sealed record DraftPerson(string Xref, string Name, string? Sex);

/// <summary>
/// A link the finding establishes, in the dialect's own terms: <see cref="Spouse"/> (Person and Other as partners),
/// <see cref="Child"/> (Other is a child of Family, seeded by Husb and Wife; with From the child leaves that family),
/// <see cref="Parent"/> (Other is Person's parent in Role), or a link the finding removes:
/// <see cref="UnlinkChild"/> (Other leaves Family) and <see cref="UnlinkParent"/> (Person loses the parent in Role).
/// A removal carries no citation: the evidence belongs in a note that cites the finding.
/// </summary>
public sealed record DraftRelation(
    string Kind, string Person, string Other, string? Family, string? Role, string? Husb, string? Wife, int Quay)
{
    public const string Spouse = "spouse";
    public const string Child = "child";
    public const string Parent = "parent";
    public const string UnlinkChild = "unlink-child";
    public const string UnlinkParent = "unlink-parent";

    public IReadOnlyList<string> NotDuplicateOf { get; init; } = [];

    /// <summary>A narrower extract than the finding's for this link's citation.</summary>
    public string? Excerpt { get; init; }

    /// <summary>For a <see cref="Child"/> relation, the existing family the child is moved out of.</summary>
    public string? From { get; init; }

    public bool IsRemoval => Kind is UnlinkChild or UnlinkParent;
}

public sealed record DraftFinding(
    string Id, DraftSource Source, string Extract, DraftTarget Target,
    IReadOnlyList<DraftFact> Facts, IReadOnlyList<DraftNote> Notes)
{
    public IReadOnlyList<DraftPerson> People { get; init; } = [];
    public IReadOnlyList<DraftRelation> Relations { get; init; } = [];

    /// <summary>Why the records concern this person and not a same-named rival, exactly as correlation wrote it; carried into the changeset, never judged.</summary>
    public JsonObject? Argument { get; init; }
}

/// <summary>Something the compiler did or left undone; <see cref="Kind"/> is one of <see cref="DraftDecisionKinds.All"/>.</summary>
public sealed record DraftDecision(string Kind, string Detail, IReadOnlyList<string> Findings);

public static class DraftDecisionKinds
{
    public const string ReusedSource = "reused-source";
    public const string MintedSource = "minted-source";
    public const string MintedPerson = "minted-person";
    public const string MergedCitation = "merged-citation";
    public const string KeptCitation = "kept-citation";
    public const string VerificationNote = "verification-note";
    public const string DroppedSeed = "dropped-seed";
    public const string Resolved = "resolved";
    public const string Open = "open";

    public static readonly IReadOnlyList<string> All =
        [ReusedSource, MintedSource, MintedPerson, MergedCitation, KeptCitation, VerificationNote, DroppedSeed, Resolved, Open];
}

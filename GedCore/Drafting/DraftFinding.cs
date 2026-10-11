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
/// </summary>
public sealed record DraftFact(
    string Record, string Tag, string? Date, string? Place, int Quay,
    string? Class, string? Information, string? Excerpt);

public sealed record DraftNote(string Record, string Text);

public sealed record DraftTarget(string Xref, string? Card, string? Name);

public sealed record DraftFinding(
    string Id, DraftSource Source, string Extract, DraftTarget Target,
    IReadOnlyList<DraftFact> Facts, IReadOnlyList<DraftNote> Notes);

/// <summary>Something the compiler did or left undone: "reused-source", "minted-source", "merged-citation", "verification-note", "open".</summary>
public sealed record DraftDecision(string Kind, string Detail, IReadOnlyList<string> Findings);

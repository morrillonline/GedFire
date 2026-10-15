using GedCore.Validate;
using GedFire.TargetSelection;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// research_brief's result shape: everything a caller otherwise assembles from
// get_record + get_records(kin) + a duplicate pass + select_targets, in one
// response with kin summarized to one line each. Pure data;
// ResearchBriefBuilder fills it.
// ---------------------------------------------------------------------------

public sealed record ResearchBrief
{
    public required string Xref { get; init; }

    /// <summary>The normalized card type the caller asked to emphasize, or null.</summary>
    public string? CardType { get; init; }

    /// <summary>The full record detail get_record returns for this person.</summary>
    public required PersonRecord Person { get; init; }

    public required ResearchBriefKin Kin { get; init; }

    /// <summary>Exactly the targets select_targets would report for this person, duplicates not annotated here.</summary>
    public required IReadOnlyList<SelectionTarget> Gaps { get; init; }

    /// <summary>Up to three best GEN301 duplicate candidates, as select_targets computes them.</summary>
    public required IReadOnlyList<PossibleDuplicateEntry> PossibleDuplicateOf { get; init; }

    public required int PossibleDuplicateCount { get; init; }

    /// <summary>Plausibility findings anchored on this person or their families, as the document stands.</summary>
    public required IReadOnlyList<ResearchBriefFinding> Plausibility { get; init; }

    public required ResearchBriefNotes Notes { get; init; }

    /// <summary>Card-type-specific extras; present only when cardType was given.</summary>
    public ResearchBriefEmphasis? Emphasis { get; init; }
}

/// <summary>A one-line person summary: enough to decide whether the full record is worth fetching.</summary>
public sealed record KinSummary(string Xref, string Name, string? Born, string? Died);

public sealed record SpouseSummary(
    string FamilyXref,
    KinSummary? Spouse,
    string? MarriageDate,
    string? MarriagePlace);

public sealed record ResearchBriefKin(
    IReadOnlyList<KinSummary> Parents,
    IReadOnlyList<KinSummary> Siblings,
    IReadOnlyList<SpouseSummary> Spouses,
    IReadOnlyList<KinSummary> Children);

public sealed record ResearchBriefFinding(string Code, string Severity, string Message, string? Xref)
{
    public static ResearchBriefFinding From(GedDiagnostic diagnostic) =>
        new(diagnostic.Code, diagnostic.Severity.ToString(), diagnostic.Message, diagnostic.Xref);
}

public sealed record FamilyNotes(string Xref, IReadOnlyList<string> Texts);

public sealed record ResearchBriefNotes(
    IReadOnlyList<string> Person,
    IReadOnlyList<FamilyNotes> Families);

/// <summary>A dated or placed fact cited for emphasis, e.g. a census that bounds a death.</summary>
public sealed record BriefFact(string Tag, string? Date, string? Place);

public sealed record ResearchBriefEmphasis
{
    /// <summary>For new-spouse: this person's children in families where the other parent is unrecorded.</summary>
    public IReadOnlyList<KinSummary>? ChildrenWithUnknownOtherParent { get; init; }

    /// <summary>For enrich-birth and enrich-person: christenings, censuses, and own marriages that bound the birth.</summary>
    public IReadOnlyList<BriefFact>? BirthBoundingFacts { get; init; }

    /// <summary>For enrich-death and enrich-person: burial, will, probate, censuses, and other dated facts that bound the death.</summary>
    public IReadOnlyList<BriefFact>? DeathBoundingFacts { get; init; }
}

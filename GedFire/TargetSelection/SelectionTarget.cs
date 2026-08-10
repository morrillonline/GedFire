namespace GedFire.TargetSelection;

/// <summary>
/// One self-contained research target — enough detail that an agent can
/// immediately start work with no further context (docs/design/target-
/// selection.md, "Problem" and the 2026-08-09 wanted.json schema
/// revision: cardType/difficulty/score added, gap/goal/queries dropped).
/// Exactly one of <see cref="KnownParent"/>, <see cref="SpouseFamily"/>,
/// <see cref="Enrichment"/> is populated, matching <see cref="CardType"/>.
/// </summary>
public sealed record SelectionTarget
{
    public required string Xref { get; init; }
    public required string Name { get; init; }
    public required string Surname { get; init; }
    public string? Born { get; init; }
    public string? BirthPlace { get; init; }
    public string? Died { get; init; }

    public required string CardType { get; init; }
    public required int NominalPoints { get; init; }
    public required DifficultyEntry Difficulty { get; init; }

    /// <summary>NominalPoints + Difficulty's bonus: the total expected payoff shown to the researcher.</summary>
    public required int Score { get; init; }

    /// <summary>New parent only: the one recorded parent, if any.</summary>
    public KnownParentEntry? KnownParent { get; init; }

    /// <summary>New child only: the spouse family that has no children recorded.</summary>
    public SpouseFamilyEntry? SpouseFamily { get; init; }

    /// <summary>Enrich person only: which fact and which parts of it are inexact.</summary>
    public EnrichmentEntry? Enrichment { get; init; }
}

public sealed record DifficultyEntry
{
    public required DifficultyBand Band { get; init; }
    public required int EraWeight { get; init; }
    public required int GeoWeight { get; init; }
    public required int ContextAdjustment { get; init; }
}

public sealed record KnownParentEntry
{
    public required string Xref { get; init; }
    public required string Name { get; init; }
}

public sealed record SpouseFamilyEntry
{
    public required string FamilyXref { get; init; }
    public string? SpouseXref { get; init; }
    public string? SpouseName { get; init; }
}

public sealed record EnrichmentEntry
{
    /// <summary>"Birth", "Marriage", or "Death".</summary>
    public required string Fact { get; init; }
    public string? CurrentDate { get; init; }
    public string? CurrentPlace { get; init; }
    public required bool MissingDate { get; init; }
    public required bool MissingPlace { get; init; }
}

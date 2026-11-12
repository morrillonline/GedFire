namespace GedFire.TargetSelection;

/// <summary>
/// The fixed set of card types. Each carries its own flat per-unit point
/// value; card types do not group into tiers. Biography is the one card that
/// is not a gap: every person can earn it again with a new citation.
/// </summary>
public enum CardType
{
    NewParent,
    NewSpouse,
    NewChild,
    EnrichPerson,
    Biography,
}

public static class CardTypeExtensions
{
    /// <summary>The display string written to wanted.json and shown to the researcher.</summary>
    public static string Display(this CardType type) => type switch
    {
        CardType.NewParent => "New parent",
        CardType.NewSpouse => "New spouse",
        CardType.NewChild => "New child",
        CardType.EnrichPerson => "Enrich person",
        CardType.Biography => "Biography",
        _ => type.ToString(),
    };
}

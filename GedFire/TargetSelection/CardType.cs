namespace GedFire.TargetSelection;

/// <summary>
/// The fixed set of detectable gap types (docs/design/target-selection.md,
/// "Card type — determines point value"). Each carries its own flat
/// per-unit point value; card types no longer group into tiers.
/// </summary>
public enum CardType
{
    NewParent,
    NewSpouse,
    NewChild,
    EnrichPerson,
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
        _ => type.ToString(),
    };
}

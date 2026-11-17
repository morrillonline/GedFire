namespace GedFire.TargetSelection;

/// <summary>When a childless couple almost certainly had no children together, so a New child card is withheld.</summary>
/// <param name="MaxMotherAgeAtMarriage">The wife's youngest possible age at the marriage at or above which the card is withheld.</param>
/// <param name="MinMarriageYears">The marriage's longest possible length, in years, below which the card is withheld.</param>
public sealed record NewChildLimits(int MaxMotherAgeAtMarriage = 50, int MinMarriageYears = 1)
{
    public static readonly NewChildLimits Default = new();
}

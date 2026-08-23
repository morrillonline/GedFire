namespace GedFire.TargetSelection;

/// <summary>
/// The four difficulty bands a candidate's raw difficulty sums into.
/// Serialized by name (Common/Uncommon/Rare/Legendary).
/// </summary>
public enum DifficultyBand
{
    Common,
    Uncommon,
    Rare,
    Legendary,
}

/// <summary>
/// Difficulty score for one candidate: the three additive GED-only signals
/// (era weight, geography weight, anchor-strength context adjustment),
/// their sum's band, and the points bonus that band adds to the card's
/// nominal points.
/// </summary>
public sealed record DifficultyScore
{
    public required int EraWeight { get; init; }
    public required int GeoWeight { get; init; }
    public required int ContextAdjustment { get; init; }
    public required DifficultyBand Band { get; init; }
    public required int Bonus { get; init; }

    /// <summary>Sum of the three component weights (0-7); exposed for auditability.</summary>
    public int RawDifficulty => EraWeight + GeoWeight + ContextAdjustment;

    /// <summary>
    /// Bands the sum of the three weights per the design's table:
    /// 0 -> Common (+0), 1-2 -> Uncommon (+5), 3-4 -> Rare (+10), 5-7 -> Legendary (+15).
    /// </summary>
    public static DifficultyScore Compute(int eraWeight, int geoWeight, int contextAdjustment)
    {
        int raw = eraWeight + geoWeight + contextAdjustment;
        var (band, bonus) = raw switch
        {
            0 => (DifficultyBand.Common, 0),
            1 or 2 => (DifficultyBand.Uncommon, 5),
            3 or 4 => (DifficultyBand.Rare, 10),
            _ => (DifficultyBand.Legendary, 15),
        };
        return new DifficultyScore
        {
            EraWeight = eraWeight,
            GeoWeight = geoWeight,
            ContextAdjustment = contextAdjustment,
            Band = band,
            Bonus = bonus,
        };
    }
}

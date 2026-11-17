namespace GedFire.TargetSelection;

/// <summary>The tunable thresholds of gap detection, and whether withheld cards are drawn anyway.</summary>
public sealed record GapRules
{
    public static readonly GapRules Default = new();

    public HonestRange HonestRange { get; init; } = HonestRange.Default;

    public NewChildLimits NewChild { get; init; } = NewChildLimits.Default;

    /// <summary>Draw every card a rule would withhold, so the owner can force one; nothing is reported as suppressed.</summary>
    public bool IncludeSuppressed { get; init; }
}

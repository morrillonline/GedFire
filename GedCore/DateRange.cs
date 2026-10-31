using GedCore.Drafting;

namespace GedCore;

/// <summary>How wide a GEDCOM BET … AND … range is, reading an end without a day or month as its whole month or year.</summary>
public static class DateRange
{
    /// <summary>True for a BET … AND … range whose earliest and latest days are at most <paramref name="months"/> months apart.</summary>
    public static bool SpansAtMost(string? date, int months) =>
        !string.IsNullOrWhiteSpace(date)
        && Specificity.BetweenDays(date) is var (earliest, latest)
        && latest <= earliest.AddMonths(months);
}

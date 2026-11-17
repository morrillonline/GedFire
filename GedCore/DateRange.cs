using GedCore.Drafting;

namespace GedCore;

/// <summary>How wide a GEDCOM date is, reading an end without a day or month as its whole month or year.</summary>
public static class DateRange
{
    /// <summary>How many years either side of its year an ABT, CAL or EST date may fall.</summary>
    public const int SoftDateYears = 2;

    /// <summary>True for a BET … AND … range whose earliest and latest days are at most <paramref name="months"/> months apart.</summary>
    public static bool SpansAtMost(string? date, int months) =>
        !string.IsNullOrWhiteSpace(date)
        && Specificity.BetweenDays(date) is var (earliest, latest)
        && latest <= earliest.AddMonths(months);

    /// <summary>The earliest day any reading of <paramref name="date"/> allows; null when unknown, unreadable, or unbounded below (BEF, TO).</summary>
    public static DateOnly? Earliest(string? date) =>
        string.IsNullOrWhiteSpace(date) ? null : Specificity.Bounds(date, SoftDateYears)?.Earliest;

    /// <summary>The latest day any reading of <paramref name="date"/> allows; null when unknown, unreadable, or unbounded above (AFT, FROM).</summary>
    public static DateOnly? Latest(string? date) =>
        string.IsNullOrWhiteSpace(date) ? null : Specificity.Bounds(date, SoftDateYears)?.Latest;
}

using GedCore;
using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// A childless couple who almost certainly had no children together: on every reading of the dates, the wife was past
/// childbearing age at the marriage, or a death or divorce ended it too soon. An unknown date is no evidence, and a child
/// of either spouse born after the marriage is evidence against.
/// </summary>
static class ChildlessCouple
{
    public static string? WithholdReason(GedFamily family, NewChildLimits limits)
    {
        if (family.Marriage?.Date is not { Length: > 0 } married || DateRange.Earliest(married) is not { } start) return null;
        if (AnyChildBornSince(family, start)) return null;
        return PastChildbearingAge(family.Wife?.Birth?.Date, married, start, limits.MaxMotherAgeAtMarriage)
            ?? EndedTooSoon(family, married, start, limits.MinMarriageYears);
    }

    // A birth with no upper bound (AFT, FROM) may fall after the marriage.
    static bool AnyChildBornSince(GedFamily family, DateOnly start) =>
        new[] { family.Husband, family.Wife }.OfType<GedIndividual>()
            .SelectMany(spouse => spouse.FamSpouse)
            .SelectMany(f => f.Children)
            .Any(child => DateRange.Latest(child.Birth?.Date) is { } latest
                ? latest >= start
                : DateRange.Earliest(child.Birth?.Date) is not null);

    // The youngest age the dates allow: her latest possible birth against the earliest possible marriage.
    static string? PastChildbearingAge(string? born, string married, DateOnly start, int maxAge)
    {
        if (DateRange.Latest(born) is not { } latestBirth) return null;
        int youngest = FullYears(latestBirth, start);
        if (youngest < maxAge) return null;

        int nominal = GedDate.ParseYear(married) - GedDate.ParseYear(born);
        bool approximate = !IsOneDay(born) || !IsOneDay(married);
        string age = approximate && nominal > youngest ? $"about {nominal} at marriage, at least {youngest}" : $"{youngest} at marriage";
        return $"wife aged {age} (born {born}, married {married}), at or past maxMotherAgeAtMarriage {maxAge}";
    }

    static bool IsOneDay(string? date) => DateRange.Earliest(date) is { } earliest && earliest == DateRange.Latest(date);

    // The longest length the dates allow: the earliest possible marriage to the latest possible end of the first ending.
    static string? EndedTooSoon(GedFamily family, string married, DateOnly start, int minYears)
    {
        var first = Endings(family)
            .Select(e => (e.Label, e.Date, Latest: DateRange.Latest(e.Date)))
            .Where(e => e.Latest is not null)
            .OrderBy(e => e.Latest)
            .FirstOrDefault();
        // An ending dated before the marriage is a data error, not evidence.
        if (first.Latest is not { } end || end < start || end >= start.AddYears(minYears)) return null;

        return $"marriage lasted {Duration(WholeMonths(start, end))} (married {married}, {first.Label} {first.Date}), " +
            $"under minMarriageYears {minYears}";
    }

    // A burial dates the death no earlier than it happened, so it also bounds the marriage.
    static IEnumerable<(string Label, string? Date)> Endings(GedFamily family)
    {
        yield return ("husband died", family.Husband?.Death?.Date);
        yield return ("husband buried", family.Husband?.Burial?.Date);
        yield return ("wife died", family.Wife?.Death?.Date);
        yield return ("wife buried", family.Wife?.Burial?.Date);
        yield return ("divorced", family.Divorce?.Date);
    }

    static int FullYears(DateOnly from, DateOnly to)
    {
        int years = to.Year - from.Year;
        return to < from.AddYears(years) ? years - 1 : years;
    }

    static int WholeMonths(DateOnly from, DateOnly to)
    {
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        return to.Day < from.Day ? months - 1 : months;
    }

    static string Duration(int months) => months switch
    {
        0 => "less than a month",
        1 => "at most 1 month",
        _ => $"at most {months} months",
    };
}

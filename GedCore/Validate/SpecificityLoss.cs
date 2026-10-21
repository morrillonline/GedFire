namespace GedCore.Validate;

/// <summary>
/// Detects a replacement value that is a less specific form of the value it
/// replaces: a place whose jurisdiction list is a proper suffix of the held
/// place hierarchy, or a date that drops the held date's day (or day and
/// month). A conflicting value and a more detailed value are never less
/// specific. Backs the GEN201 advisory <c>CreateOrUpdateVitalOp</c> raises at
/// apply time — a warning only, nothing is blocked.
/// </summary>
public static class SpecificityLoss
{
    public const string Code = "GEN201";

    public const string Advice = "losing specificity is not recommended except when correcting an error";

    /// <summary>
    /// "Canada" for a held "Pembroke, Yarmouth, Nova Scotia, Canada": the
    /// proposed jurisdiction list is a proper suffix of the held one, compared
    /// segment by segment, trimmed and case-insensitive.
    /// </summary>
    public static bool IsLessSpecificPlace(string? held, string? proposed)
    {
        var heldParts = SplitPlace(held);
        var proposedParts = SplitPlace(proposed);
        if (proposedParts.Count == 0 || proposedParts.Count >= heldParts.Count) return false;

        int offset = heldParts.Count - proposedParts.Count;
        for (int i = 0; i < proposedParts.Count; i++)
            if (!string.Equals(heldParts[offset + i], proposedParts[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>
    /// "1874" or "NOV 1874" for a held "05 NOV 1874", or "1874" for a held
    /// "NOV 1874": both sides are plain Gregorian dates and the proposed one
    /// is the held one with the day (or the day and month) dropped. Day
    /// padding is insignificant. Qualified, ranged, periodic, dual-year, and
    /// calendar-prefixed dates never trigger.
    /// </summary>
    public static bool IsLessSpecificDate(string? held, string? proposed)
    {
        if (ParsePlain(held) is not { } h || ParsePlain(proposed) is not { } p) return false;
        if (p.Year != h.Year || p.Day is not null) return false;
        if (p.Month is not null) return h.Day is not null && p.Month == h.Month;
        return h.Month is not null;
    }

    private static List<string> SplitPlace(string? place) =>
        string.IsNullOrWhiteSpace(place)
            ? []
            : [.. place.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0)];

    private static readonly string[] Months =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    /// <summary>
    /// Parse a plain [day] [month] year date, or null for anything else — a
    /// qualifier or calendar keyword, a range or period, or a dual year all
    /// leave a token this shape cannot resolve, so the rule simply does not
    /// trigger rather than guessing.
    /// </summary>
    private static (int? Day, int? Month, int Year)? ParsePlain(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        string[] tokens = date.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 3) return null;

        if (!int.TryParse(tokens[^1], out int year) || year <= 0) return null;

        int? month = null;
        if (tokens.Length >= 2)
        {
            int m = Array.FindIndex(Months, name => name.Equals(tokens[^2], StringComparison.OrdinalIgnoreCase)) + 1;
            if (m == 0) return null;
            month = m;
        }

        int? day = null;
        if (tokens.Length == 3)
        {
            if (!int.TryParse(tokens[0], out int d) || d is < 1 or > 31) return null;
            day = d;
        }

        return (day, month, year);
    }
}

using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// "Exact date" / "exact place" and the era/geography difficulty weights,
/// as defined by docs/design/target-selection.md ("Exact date, exact
/// place" and "Difficulty — a second, independent axis"). A fact is exact
/// only when both its date and its place pass these tests.
/// </summary>
public static class ExactnessRules
{
    static readonly HashSet<string> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
        "JUL", "AUG", "SEP", "OCT", "NOV", "DEC",
    };

    /// <summary>
    /// True when a day, a month, and a year are all present and the DATE
    /// carries no GEDCOM approximation qualifier. A dual-year Old
    /// Style/New Style date ("2 FEB 1681/82") is exact: the day and month
    /// are still both present, and the "/82" is a calendar-reconciliation
    /// notation, not a qualifier. Reuses <see cref="GedDate.Qualifier"/> for
    /// qualifier detection so both places agree on what counts as one.
    /// </summary>
    public static bool IsExactDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return false;
        if (GedDate.Qualifier(date) is not null) return false;

        var tokens = date.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 3) return false;
        if (!int.TryParse(tokens[0], out int day) || day is < 1 or > 31) return false;
        if (!Months.Contains(tokens[1])) return false;

        // Dual-year "1681/82": only the left/old-style year needs to parse.
        string yearToken = tokens[2].Split('/')[0];
        return int.TryParse(yearToken, out _);
    }

    /// <summary>
    /// True when a city/town <b>and</b> a state (or, outside the US, the
    /// equivalent first-level jurisdiction below the country) are both
    /// present in the PLAC value's comma hierarchy. Country is optional.
    /// A blunt heuristic by design — see the design doc's own caveat about
    /// unusual PLAC formats.
    /// </summary>
    public static bool IsExactPlace(string? place)
    {
        if (string.IsNullOrWhiteSpace(place)) return false;
        return CommaParts(place).Length >= 2;
    }

    /// <summary>
    /// Difficulty weight for the era of a core-event year, per the design's
    /// era-weight table. An unknown year is treated leniently as weight 0 —
    /// mirroring how "no place recorded" defaults to 0 for geography below,
    /// since the design has no separate "unknown" bucket for either signal.
    /// </summary>
    public static int EraWeight(int? year) => year switch
    {
        null or <= 0 => 0,
        >= 1900 => 0,
        >= 1800 => 1,
        >= 1700 => 2,
        _ => 3,
    };

    /// <summary>
    /// Difficulty weight for the region of a core-event place, per the
    /// design's geography-weight table: parsed from the last comma-separated
    /// token against a small static list. Blunt by design — see the design
    /// doc's own caveat about unusual PLAC formats.
    /// </summary>
    public static int GeographyWeight(string? place)
    {
        if (string.IsNullOrWhiteSpace(place)) return 0; // no place recorded -> presumptively local
        var parts = CommaParts(place);
        if (parts.Length == 0) return 0;
        string region = parts[^1];

        if (UsAndCanada.Contains(region)) return 0;
        if (BritishIslesAndIreland.Contains(region)) return 1;
        if (OtherEurope.Contains(region)) return 2;
        return 3;
    }

    static string[] CommaParts(string place) =>
        [.. place.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0)];

    static readonly HashSet<string> UsAndCanada = new(StringComparer.OrdinalIgnoreCase)
    {
        "United States", "United States of America", "USA", "US", "U.S.", "U.S.A.",
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut",
        "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa",
        "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan",
        "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada",
        "New Hampshire", "New Jersey", "New Mexico", "New York", "North Carolina",
        "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island",
        "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont",
        "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming",
        "District of Columbia", "D.C.",
        "Canada", "Ontario", "Quebec", "Nova Scotia", "New Brunswick", "Manitoba",
        "British Columbia", "Prince Edward Island", "Saskatchewan", "Alberta",
        "Newfoundland and Labrador", "Northwest Territories", "Yukon", "Nunavut",
    };

    static readonly HashSet<string> BritishIslesAndIreland = new(StringComparer.OrdinalIgnoreCase)
    {
        "England", "Wales", "Scotland", "Ireland", "Northern Ireland",
        "United Kingdom", "UK", "U.K.", "Great Britain",
    };

    static readonly HashSet<string> OtherEurope = new(StringComparer.OrdinalIgnoreCase)
    {
        "France", "Germany", "Italy", "Spain", "Portugal", "Netherlands", "Holland",
        "Belgium", "Switzerland", "Austria", "Sweden", "Norway", "Denmark", "Finland",
        "Poland", "Russia", "Greece", "Hungary", "Czech Republic", "Czechoslovakia",
        "Romania", "Bulgaria", "Ukraine", "Iceland", "Luxembourg",
    };
}

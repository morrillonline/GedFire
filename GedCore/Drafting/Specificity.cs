using System.Text.RegularExpressions;
using GedCore.Matching;

namespace GedCore.Drafting;

/// <summary>
/// Orders GEDCOM dates, places and names by how much they say, so findings that give different amounts of the same
/// fact can be reconciled: the more specific value supersedes the less specific one it is consistent with.
/// </summary>
internal static partial class Specificity
{
    static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    static readonly string[] ApproximateWords =
        ["ABT", "ABOUT", "CAL", "EST", "BEF", "BEFORE", "AFT", "AFTER", "BET", "BETWEEN", "FROM", "TO"];

    sealed record Parsed(string Year, string? Month, string? Day)
    {
        public int Parts => 1 + (Month is null ? 0 : 1) + (Day is null ? 0 : 1);
    }

    /// <summary>Whether the date is a plain year, month and year, or full date, as opposed to an about, before, between or other qualified date.</summary>
    public static bool IsApproximate(string date) =>
        ApproximateWords.Contains(Normalize(date).Split(' ')[0]);

    /// <summary>True when <paramref name="a"/> may stand in place of <paramref name="b"/>: they are the same, <paramref name="a"/> is a specific date and <paramref name="b"/> an approximate one, or <paramref name="a"/> adds a month or day to <paramref name="b"/> without changing what it says.</summary>
    public static bool DateSupersedes(string a, string b)
    {
        if (SameDate(a, b)) return true;
        if (IsApproximate(a)) return false;
        if (IsApproximate(b)) return true;
        return Parse(a) is { } pa && Parse(b) is { } pb && pa.Parts >= pb.Parts && Agrees(pa, pb);
    }

    public static bool SameDate(string a, string b) =>
        Normalize(a) == Normalize(b) || (Parse(a) is { } pa && Parse(b) is { } pb && pa == pb);

    /// <summary>
    /// True when the two names are the same, or <paramref name="a"/> is <paramref name="b"/> with a given name replaced by its
    /// formal form: "William /Morrill/" supersedes "Bill /Morrill/", but not "Bob /Morrill/" or "Will /Smith/".
    /// The surname, suffix and number of given names must match; anything else is a different name.
    /// </summary>
    public static bool NameSupersedes(string a, string b)
    {
        if (SameName(a, b)) return true;
        if (SplitName(a) is not { } pa || SplitName(b) is not { } pb) return false;
        if (pa.Surname != pb.Surname || pa.Suffix != pb.Suffix || pa.Given.Length != pb.Given.Length) return false;
        return pa.Given.Zip(pb.Given).All(g => g.First == g.Second || Nicknames.Value.IsNicknameOf(g.Second, g.First, null));
    }

    public static bool SameName(string a, string b) =>
        PersonNameNormalizer.Normalize(StripSlashes(a)) == PersonNameNormalizer.Normalize(StripSlashes(b));

    static readonly Lazy<NicknameDirectory> Nicknames = new(NicknameDirectory.LoadEmbedded);

    sealed record NameParts(string[] Given, string Surname, string Suffix);

    // "Given Names /Surname/ Suffix" in GEDCOM form; a name without a surname in slashes cannot be compared part by part.
    static NameParts? SplitName(string name)
    {
        int open = name.IndexOf('/');
        int close = open < 0 ? -1 : name.IndexOf('/', open + 1);
        if (open < 0 || close < 0) return null;

        string[] given = [.. PersonNameNormalizer.Normalize(name[..open]).Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        return given.Length == 0 ? null : new NameParts(given, PersonNameNormalizer.Normalize(name[(open + 1)..close]), PersonNameNormalizer.Normalize(name[(close + 1)..]));
    }

    static string StripSlashes(string name) => name.Replace("/", " ");

    /// <summary>Whether an approximate date can describe the same time as a specific one, by year; the approximate date's citation is attached only when it can.</summary>
    public static bool Fits(string approximate, string specific)
    {
        if (GedDate.ParseValue(approximate) is not { } value || Parse(specific) is not { } parsed) return false;
        if (!int.TryParse(parsed.Year.Split('/')[0], out int year)) return false;
        return value.Kind switch
        {
            GedDateKind.About or GedDateKind.Calculated or GedDateKind.Estimated => Math.Abs(value.FromYear - year) <= 1,
            GedDateKind.Before => year <= value.FromYear,
            GedDateKind.After => year >= value.FromYear,
            GedDateKind.Between => value.FromYear <= year && year <= value.ToYear,
            GedDateKind.Period => (value.FromYear == 0 || value.FromYear <= year) && (value.ToYear == 0 || year <= value.ToYear),
            _ => false,
        };
    }

    /// <summary>True when the two places are the same, or <paramref name="a"/> names every jurisdiction of <paramref name="b"/>, in the same order, and more: "Oakland, Kennebec, Maine" supersedes "Oakland, Maine" and "Maine".</summary>
    public static bool PlaceSupersedes(string a, string b)
    {
        var pa = Jurisdictions(a);
        var pb = Jurisdictions(b);
        if (pb.Length > pa.Length) return false;

        int next = 0;
        foreach (string jurisdiction in pa)
            if (next < pb.Length && string.Equals(jurisdiction, pb[next], StringComparison.OrdinalIgnoreCase)) next++;
        return next == pb.Length;
    }

    public static bool SamePlace(string a, string b) => Jurisdictions(a).SequenceEqual(Jurisdictions(b), StringComparer.OrdinalIgnoreCase);

    static string[] Jurisdictions(string place) =>
        [.. place.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0)];

    static bool Agrees(Parsed specific, Parsed general) =>
        specific.Year == general.Year
        && (general.Month is null || specific.Month == general.Month)
        && (general.Day is null || specific.Day == general.Day);

    static string Normalize(string date) => WhiteSpace().Replace(date.Trim(), " ").ToUpperInvariant();

    static Parsed? Parse(string date)
    {
        string[] tokens = Normalize(date).Split(' ');
        if (tokens.Length is < 1 or > 3 || !YearToken().IsMatch(tokens[^1])) return null;

        string? month = null, day = null;
        if (tokens.Length >= 2 && MonthOf(tokens[^2]) is { } found) month = found;
        else if (tokens.Length >= 2) return null;

        if (tokens.Length == 3)
        {
            if (!DayToken().IsMatch(tokens[0])) return null;
            day = int.Parse(tokens[0]).ToString();
        }
        return new Parsed(tokens[^1], month, day);
    }

    static string? MonthOf(string token) =>
        token.Length >= 3 ? Months.FirstOrDefault(m => token.StartsWith(m, StringComparison.Ordinal)) : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"^\d{1,4}(/\d{1,2})?$")]
    private static partial Regex YearToken();

    [GeneratedRegex(@"^\d{1,2}$")]
    private static partial Regex DayToken();
}

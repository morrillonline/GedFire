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

    public static bool SameDate(string a, string b) => Canonical(a) == Canonical(b);

    /// <summary>Dates that can both be true: one refines the other, or an approximate one fits a specific one.</summary>
    public static bool Consistent(string a, string b)
    {
        bool approxA = IsApproximate(a), approxB = IsApproximate(b);
        if (IsBound(a) && IsBound(b)) return CombineBounds(a, b, out _) != BoundOverlap.Disjoint;
        if (approxA != approxB) return approxA ? Fits(a, b) : Fits(b, a);
        return approxA || DateSupersedes(a, b) || DateSupersedes(b, a);
    }

    static readonly Dictionary<string, string> ShortQualifiers = new()
    {
        ["ABOUT"] = "ABT", ["BEFORE"] = "BEF", ["AFTER"] = "AFT", ["BETWEEN"] = "BET",
    };

    // One spelling per date: "BEFORE 01 Sept 1796" reads "BEF 1 SEP 1796".
    static string Canonical(string date)
    {
        string normalized = Normalize(date);
        string[] tokens = normalized.Split(' ');
        string head = ShortQualifiers.GetValueOrDefault(tokens[0], tokens[0]);
        if (!ApproximateWords.Contains(head)) return CanonicalPart(normalized);
        return $"{head} {string.Join(" AND ", string.Join(' ', tokens.Skip(1)).Split(" AND ").Select(CanonicalPart))}";
    }

    static string CanonicalPart(string date) => Parse(date) is { } parsed ? Format(parsed) : date;

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
        if (GedDate.ParseValue(Canonical(approximate)) is not { } value || Parse(specific) is not { } parsed) return false;
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

    /// <summary>A hard bound: BEF, AFT or BET … AND, which a date either falls within or not. FROM/TO periods are spans, not bounds.</summary>
    public static bool IsBound(string date) => GedDate.ParseValue(Canonical(date))?.Kind is GedDateKind.Before or GedDateKind.After or GedDateKind.Between;

    /// <summary>A soft approximation: ABT, CAL or EST, which has no defined tolerance.</summary>
    public static bool IsSoft(string date) => GedDate.ParseValue(Canonical(date))?.Kind is GedDateKind.About or GedDateKind.Calculated or GedDateKind.Estimated;

    /// <summary>The earliest and latest days a BET … AND … range admits; an end without a day or month stands for its whole month or year.</summary>
    public static (DateOnly Earliest, DateOnly Latest)? BetweenDays(string date) =>
        Ends(date) is { Lower: { } lower, Upper: { } upper } && DayOf(lower, last: false) is { } earliest && DayOf(upper, last: true) is { } latest
            ? (earliest, latest)
            : null;

    static DateOnly? DayOf(Parsed date, bool last)
    {
        if (!int.TryParse(date.Year.Split('/')[0], out int year) || year is < 1 or > 9999) return null;
        int month = date.Month is null ? (last ? 12 : 1) : Array.IndexOf(Months, date.Month) + 1;
        int days = DateTime.DaysInMonth(year, month);
        int day = date.Day is null ? (last ? days : 1) : int.Parse(date.Day);
        return day >= 1 && day <= days ? new DateOnly(year, month, day) : null;
    }

    /// <summary>What two hard bounds say together.</summary>
    public enum BoundOverlap { Combined, Disjoint, Unknown }

    /// <summary>
    /// Intersects two hard bounds: "AFT 1795" and "BEF 27 SEP 1796" give "BET 1795 AND 27 SEP 1796"; "BEF 1800" and "BEF 1797"
    /// give "BEF 1797". <see cref="BoundOverlap.Disjoint"/> when no date satisfies both, <see cref="BoundOverlap.Unknown"/> when
    /// the ends cannot be ordered ("1796" against "SEP 1796").
    /// </summary>
    public static BoundOverlap CombineBounds(string a, string b, out string? combined)
    {
        combined = null;
        if (Ends(a) is not { } ea || Ends(b) is not { } eb) return BoundOverlap.Unknown;

        if (!Later(ea.Lower, eb.Lower, out var lower) || !Earlier(ea.Upper, eb.Upper, out var upper)) return BoundOverlap.Unknown;
        if (lower is not null && upper is not null)
        {
            if (Compare(lower, upper) is not { } order) return BoundOverlap.Unknown;
            if (order >= 0) return BoundOverlap.Disjoint;
        }

        combined = (lower, upper) switch
        {
            ({ } l, { } u) => $"BET {Format(l)} AND {Format(u)}",
            (null, { } u) => $"BEF {Format(u)}",
            ({ } l, null) => $"AFT {Format(l)}",
            _ => null,
        };
        return combined is null ? BoundOverlap.Unknown : BoundOverlap.Combined;
    }

    sealed record BoundEnds(Parsed? Lower, Parsed? Upper);

    static BoundEnds? Ends(string date)
    {
        string[] tokens = Normalize(date).Split(' ');
        string rest = string.Join(' ', tokens.Skip(1));
        switch (tokens[0])
        {
            case "BEF" or "BEFORE": return Parse(rest) is { } u ? new BoundEnds(null, u) : null;
            case "AFT" or "AFTER": return Parse(rest) is { } l ? new BoundEnds(l, null) : null;
            case "BET" or "BETWEEN":
                int and = Array.IndexOf(tokens, "AND");
                if (and < 2) return null;
                return Parse(string.Join(' ', tokens[1..and])) is { } from && Parse(string.Join(' ', tokens[(and + 1)..])) is { } to
                    ? new BoundEnds(from, to) : null;
            default: return null;
        }
    }

    // The later of two lower ends (a missing end imposes nothing); false when they cannot be ordered.
    static bool Later(Parsed? a, Parsed? b, out Parsed? later)
    {
        later = a ?? b;
        if (a is null || b is null) return true;
        if (Compare(a, b) is not { } order) return false;
        later = order >= 0 ? a : b;
        return true;
    }

    static bool Earlier(Parsed? a, Parsed? b, out Parsed? earlier)
    {
        earlier = a ?? b;
        if (a is null || b is null) return true;
        if (Compare(a, b) is not { } order) return false;
        earlier = order <= 0 ? a : b;
        return true;
    }

    // Orders two dates; null when one says less than the other about the year they share.
    static int? Compare(Parsed a, Parsed b)
    {
        if (!int.TryParse(a.Year.Split('/')[0], out int ya) || !int.TryParse(b.Year.Split('/')[0], out int yb)) return null;
        if (ya != yb) return ya.CompareTo(yb);
        if (a.Month is null || b.Month is null) return a.Month == b.Month ? 0 : null;
        int byMonth = Array.IndexOf(Months, a.Month).CompareTo(Array.IndexOf(Months, b.Month));
        if (byMonth != 0) return byMonth;
        if (a.Day is null || b.Day is null) return a.Day == b.Day ? 0 : null;
        return int.Parse(a.Day).CompareTo(int.Parse(b.Day));
    }

    static string Format(Parsed date) => string.Join(' ', new[] { date.Day, date.Month, date.Year }.OfType<string>());

    /// <summary>True when the two places are the same, or <paramref name="a"/> names every jurisdiction of <paramref name="b"/>, in the same order, and more: "Oakland, Kennebec, Maine" supersedes "Oakland, Maine" and "Maine".</summary>
    public static bool PlaceSupersedes(string a, string b)
    {
        var pa = PlaceNormalizer.Jurisdictions(a);
        var pb = PlaceNormalizer.Jurisdictions(b);
        if (pb.Count > pa.Count) return false;

        int next = 0;
        foreach (string jurisdiction in pa)
            if (next < pb.Count && jurisdiction == pb[next]) next++;
        return next == pb.Count;
    }

    /// <summary>The same jurisdictions, ignoring case, hyphens, spacing, trailing periods and a trailing County, Co. or Parish.</summary>
    public static bool SamePlace(string a, string b) => PlaceNormalizer.Jurisdictions(a).SequenceEqual(PlaceNormalizer.Jurisdictions(b));

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

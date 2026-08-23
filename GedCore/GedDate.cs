namespace GedCore;

// ---------------------------------------------------------------------------
// GEDCOM 7 date grammar: parsing (all date parsing and arithmetic live in
// GedCore, so changeset validation, HTML generation, matching, and the CLI
// can all use it without reverse references or parallel grammars) plus the
// standalone AddAge/SubtractAge/Diff arithmetic date-calc needs. Parsing
// never replaces the source date:
// GedDateValue is a derived interpretation used for matching, ordering, and
// calculation, never the document model's stored DATE payload.
// ---------------------------------------------------------------------------

public static class GedDate
{
    // Leading GEDCOM date qualifier ("ABT 1780", "BEF 1854", "BET 1850 AND
    // 1860", …), normalized, or null for an exact date. Consumers use it to
    // widen matching tolerance for approximate/bounded dates.
    public static string? Qualifier(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        string tok = dateStr.TrimStart();
        int sp = tok.IndexOf(' ');
        if (sp > 0) tok = tok[..sp];
        return tok.ToUpperInvariant() switch
        {
            "ABT" or "ABOUT" => "ABT",
            "CAL"  => "CAL",
            "EST"  => "EST",
            "BEF"  => "BEF",
            "AFT"  => "AFT",
            "BET"  => "BET",
            "FROM" => "FROM",
            "TO"   => "TO",
            _ => null,
        };
    }

    // Extract a numeric year from a raw GEDCOM date string. Delegates to
    // ParseValue's FromYear, which accepts 1-4 digit years (fixing the old
    // exactly-4-digit restriction that dropped short years like "986") while
    // keeping the same left-of-slash convention for a dual date ("1745/46"
    // -> 1745) that this method has always used — verified against the
    // GedDateTests/GedDateEdgeCaseTests fixtures and a generated-page URL
    // stability check.
    public static int ParseYear(string? dateStr) => ParseValue(dateStr)?.FromYear ?? 0;

    // Full DateTime parse (mirrors VB ParseDate). Delegates to ParseValue,
    // taking the primary/from date of whatever DateValue production matched.
    public static DateTime? Parse(string? dateStr) => ParseValue(dateStr)?.From;

    private static readonly string[] CalendarKeywords = ["GREGORIAN", "JULIAN", "FRENCH_R", "HEBREW"];

    /// <summary>
    /// Parse a raw GEDCOM DateValue per the 7.0.18 grammar: a plain date, an
    /// ABT/CAL/EST/BEF/AFT-qualified date, a BET a AND b range, or a
    /// FROM a [TO b] / TO b period — optionally calendar-prefixed
    /// (GREGORIAN/JULIAN/FRENCH_R/HEBREW; non-Gregorian calendars parse the
    /// year only, since their month names differ) and epoch-suffixed (BCE,
    /// which negates the year and leaves the DateTime unrepresentable).
    /// Returns null for anything unparseable — this never throws.
    /// </summary>
    public static GedDateValue? ParseValue(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        string[] tokens = dateStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;
        // A privatized date (legacy FTM convention) never yields a value.
        if (tokens.Any(t => t.StartsWith("PRIVATE", StringComparison.OrdinalIgnoreCase)))
            return null;

        int i = 0;
        bool nonGregorian = false;
        if (i < tokens.Length && CalendarKeywords.Contains(tokens[i].ToUpperInvariant()))
        {
            nonGregorian = !tokens[i].Equals("GREGORIAN", StringComparison.OrdinalIgnoreCase);
            i++;
        }

        string kw = i < tokens.Length ? tokens[i].TrimEnd('.').ToUpperInvariant() : "";

        switch (kw)
        {
            case "BET":
            {
                int andIdx = IndexOfKeyword(tokens, i + 1, "AND");
                if (andIdx < 0) return null;
                var from = ParseDatePart(tokens, i + 1, andIdx, nonGregorian);
                var to = ParseDatePart(tokens, andIdx + 1, tokens.Length, nonGregorian);
                if (from.Year == 0 && to.Year == 0) return null;
                return new GedDateValue(GedDateKind.Between, from.Date, to.Date, from.Year, to.Year);
            }
            case "FROM":
            {
                int toIdx = IndexOfKeyword(tokens, i + 1, "TO");
                int fromEnd = toIdx >= 0 ? toIdx : tokens.Length;
                var from = ParseDatePart(tokens, i + 1, fromEnd, nonGregorian);
                var to = toIdx >= 0
                    ? ParseDatePart(tokens, toIdx + 1, tokens.Length, nonGregorian)
                    : (Date: (DateTime?)null, Year: 0);
                if (from.Year == 0 && to.Year == 0) return null;
                return new GedDateValue(GedDateKind.Period, from.Date, to.Date, from.Year, to.Year);
            }
            case "TO":
            {
                var to = ParseDatePart(tokens, i + 1, tokens.Length, nonGregorian);
                if (to.Year == 0) return null;
                return new GedDateValue(GedDateKind.Period, null, to.Date, 0, to.Year);
            }
            case "ABT" or "ABOUT" or "CAL" or "EST" or "BEF" or "AFT":
            {
                var kind = kw switch
                {
                    "ABT" or "ABOUT" => GedDateKind.About,
                    "CAL" => GedDateKind.Calculated,
                    "EST" => GedDateKind.Estimated,
                    "BEF" => GedDateKind.Before,
                    "AFT" => GedDateKind.After,
                    _ => GedDateKind.Exact,
                };
                var from = ParseDatePart(tokens, i + 1, tokens.Length, nonGregorian);
                if (from.Year == 0) return null;
                return new GedDateValue(kind, from.Date, null, from.Year, 0);
            }
            default:
            {
                var from = ParseDatePart(tokens, i, tokens.Length, nonGregorian);
                if (from.Year == 0) return null;
                return new GedDateValue(GedDateKind.Exact, from.Date, null, from.Year, 0);
            }
        }
    }

    private static int IndexOfKeyword(string[] tokens, int start, string keyword)
    {
        for (int i = start; i < tokens.Length; i++)
            if (tokens[i].Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Parse one date phrase (day/month/year tokens in <paramref name="start"/>..
    /// <paramref name="end"/>, GEDCOM order) into a DateTime (when it fully
    /// resolves) and a year (which survives even a partial/unparseable date,
    /// e.g. an unrecognized month token). A trailing BCE epoch negates the
    /// year and leaves the DateTime null (DateTime cannot represent it).
    /// </summary>
    private static (DateTime? Date, int Year) ParseDatePart(
        string[] tokens, int start, int end, bool nonGregorian)
    {
        bool bce = end > start && tokens[end - 1].Equals("BCE", StringComparison.OrdinalIgnoreCase);
        if (bce) end--;

        string day = "", month = "", year = "";
        for (int idx = start; idx < end; idx++)
        {
            string token = tokens[idx];
            if (IsAboutQualifier(token)) continue;
            if (year == "") year = token;
            else if (month == "") { month = year; year = token; }
            else if (day == "") { day = month; month = year; year = token; }
        }
        if (year == "") return (null, 0);

        // Dual date "1745/46" (Julian 1745 = Gregorian 1746): century prefix
        // of the left year + the post-slash right year. Too short to carry a
        // century ("5/6") is unparseable rather than a crash.
        int slash = year.IndexOf('/');
        string yearForDate = year;
        if (slash >= 0)
        {
            if (slash < 2) return (null, 0);
            yearForDate = year[..(slash - 2)] + year[(slash + 1)..];
            year = year[..slash];   // FromYear/ToYear keep the left/old-style year
        }

        if (!int.TryParse(year, out int iyear) || iyear <= 0 || year.Length > 4) return (null, 0);
        if (bce) return (null, -iyear);
        if (nonGregorian) return (null, iyear);   // year only — calendar's month names differ

        if (!int.TryParse(yearForDate, out int iyearForDate)) return (null, iyear);
        try
        {
            int iday = day != "" ? int.Parse(day) : 1;
            int imonth = month != "" ? MonthNum(month) : 1;
            if (imonth == 0) return (null, iyear);
            return (new DateTime(iyearForDate, imonth, iday), iyear);
        }
        catch { return (null, iyear); }
    }

    // "About" qualifier tokens are dropped before the day/month/year shuffle.
    // Matched exactly — the old StartsWith("AB") swallowed any token that
    // merely began with those letters.
    private static bool IsAboutQualifier(string token) =>
        token.Equals("ABT",   StringComparison.OrdinalIgnoreCase) ||
        token.Equals("ABT.",  StringComparison.OrdinalIgnoreCase) ||
        token.Equals("ABOUT", StringComparison.OrdinalIgnoreCase);

    private static int MonthNum(string m)
    {
        if (m.StartsWith("JAN", StringComparison.OrdinalIgnoreCase)) return 1;
        if (m.StartsWith("FEB", StringComparison.OrdinalIgnoreCase)) return 2;
        if (m.StartsWith("MAR", StringComparison.OrdinalIgnoreCase)) return 3;
        if (m.StartsWith("APR", StringComparison.OrdinalIgnoreCase)) return 4;
        if (m.StartsWith("MAY", StringComparison.OrdinalIgnoreCase)) return 5;
        if (m.StartsWith("JUN", StringComparison.OrdinalIgnoreCase)) return 6;
        if (m.StartsWith("JUL", StringComparison.OrdinalIgnoreCase)) return 7;
        if (m.StartsWith("AUG", StringComparison.OrdinalIgnoreCase)) return 8;
        if (m.StartsWith("SEP", StringComparison.OrdinalIgnoreCase)) return 9;
        if (m.StartsWith("OCT", StringComparison.OrdinalIgnoreCase)) return 10;
        if (m.StartsWith("NOV", StringComparison.OrdinalIgnoreCase)) return 11;
        if (m.StartsWith("DEC", StringComparison.OrdinalIgnoreCase)) return 12;
        return 0;
    }

    static readonly string[] MonthAbbreviations =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    /// <summary>
    /// The canonical exact-date identity of a raw GEDCOM date string, used by
    /// the apply-time same-partner-same-date marriage conflict check:
    /// calendar, day, month, and resolved year, derived without changing
    /// either source string -- day
    /// padding is insignificant, an omitted calendar means Gregorian, and a
    /// dual year uses its resolved right-hand year. No cross-calendar
    /// conversion is invented: a non-Gregorian date's day/month/year are
    /// compared as recorded, never converted to Gregorian.
    /// </summary>
    public readonly record struct ExactDateIdentity(string Calendar, int Day, int Month, int Year);

    /// <summary>
    /// Resolve <paramref name="dateStr"/> to its <see cref="ExactDateIdentity"/>
    /// when it is one exact, fully-specified day-month-year date (an optional
    /// calendar prefix, no qualifier, range, period, or BCE epoch, and day,
    /// month, and year all present). Returns null for anything else -- a
    /// partial date, a qualified/ranged/periodic date, or an unparseable
    /// string -- so the caller's rule simply does not trigger rather than
    /// guessing.
    /// </summary>
    public static ExactDateIdentity? ExactFullDateIdentity(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        string[] tokens = dateStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string calendar = "GREGORIAN";
        int i = 0;
        if (i < tokens.Length && CalendarKeywords.Contains(tokens[i].ToUpperInvariant()))
        {
            calendar = tokens[i].ToUpperInvariant();
            i++;
        }

        // Exactly day, month, year must remain -- a qualifier keyword (ABT,
        // BEF, ...), a BET/FROM/TO range or period, a BCE epoch suffix, or a
        // partial (year-only or month-year) date all leave a token count or
        // shape this loop cannot resolve to day+month+year, so they fall
        // through to the day-parse failure below and return null.
        if (tokens.Length - i != 3) return null;

        if (!int.TryParse(tokens[i], out int day) || day < 1 || day > 31 ||
            tokens[i].Any(c => !char.IsAsciiDigit(c)))
            return null;

        int month = MonthNum(tokens[i + 1]);
        if (month == 0) return null;

        string yearToken = tokens[i + 2];
        int year;
        if (yearToken.Contains('/'))
        {
            try { year = ResolveDualYear(yearToken); }
            catch (FormatException) { return null; }
        }
        else if (!int.TryParse(yearToken, out year) || year < 1 || yearToken.Length > 4 ||
                 yearToken.Any(c => !char.IsAsciiDigit(c)))
        {
            return null;
        }

        return new ExactDateIdentity(calendar, day, month, year);
    }

    // -----------------------------------------------------------------------
    // date-calc input parsing: the "exact Gregorian day-month-year date"
    // precision date-calc's four operations require. Deliberately narrower
    // than ParseValue above: no qualifiers, ranges, periods, BCE epochs, or
    // non-Gregorian calendars, and (outside --op normalize) no dual-dated
    // year -- each is a usage error here rather than a silently accepted or
    // reinterpreted value. Successful general-purpose parsing above does not
    // imply a value is precise enough for arithmetic.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parse "D MON YYYY" (the same month grammar <see cref="ParseValue"/>
    /// uses) into a day, 1-based month, and year, with no calendar prefix,
    /// qualifier, BCE suffix, or (unless <paramref name="allowDualYear"/>)
    /// dual-dated year. Does not construct a <see cref="DateTime"/> --
    /// callers that need one do so themselves, so a caller that only needs
    /// the resolved year (<see cref="NormalizeDualDate"/>) never fails on a
    /// day/month/resolved-year combination .NET's calendar happens to
    /// reject.
    /// </summary>
    static bool TryParseExactDayMonthYear(
        string? input, bool allowDualYear, out int day, out int month, out int year, out string? error)
    {
        day = 0; month = 0; year = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "date must not be blank.";
            return false;
        }

        string[] tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 3)
        {
            error = $"'{input}' is not an exact day-month-year Gregorian date (expected \"D MON YYYY\").";
            return false;
        }

        if (!int.TryParse(tokens[0], out day) || day < 1 || day > 31 || tokens[0].Any(c => !char.IsAsciiDigit(c)))
        {
            error = $"'{tokens[0]}' is not a valid day (expected 1-31).";
            return false;
        }

        month = MonthNum(tokens[1]);
        if (month == 0)
        {
            error = $"'{tokens[1]}' is not a recognized GEDCOM month abbreviation.";
            return false;
        }

        string yearToken = tokens[2];
        if (yearToken.Contains('/'))
        {
            if (!allowDualYear)
            {
                error = $"'{input}' has a dual-dated year; only --op normalize accepts that form.";
                return false;
            }
            try
            {
                year = ResolveDualYear(yearToken);
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
        }
        else if (!int.TryParse(yearToken, out year) || year < 1 || yearToken.Length > 4 ||
                  yearToken.Any(c => !char.IsAsciiDigit(c)))
        {
            error = $"'{yearToken}' is not a valid year.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// <c>--op normalize</c>: resolve one exact dual-dated Gregorian date's
    /// right-hand year and print it back with the same day and month
    /// (<see cref="ResolveDualYear"/> -- no Julian-to-Gregorian day offset).
    /// A plain (non-dual) year passes through unchanged. Throws
    /// <see cref="FormatException"/> for anything outside the exact
    /// day-month-year grammar.
    /// </summary>
    public static string NormalizeDualDate(string input)
    {
        if (!TryParseExactDayMonthYear(input, allowDualYear: true, out int day, out int month, out int year, out string? error))
            throw new FormatException(error);
        return $"{day} {MonthAbbreviations[month - 1]} {year}";
    }

    /// <summary>
    /// Format a <see cref="DateTime"/> as "D MON YYYY" -- the same shape
    /// <see cref="NormalizeDualDate"/> prints and <see cref="ParseExactGregorianDate"/>
    /// accepts, so <c>--op add</c>/<c>sub</c>'s result can be pasted
    /// straight back into a later date-calc call.
    /// </summary>
    public static string FormatExactGregorianDate(DateTime date) =>
        $"{date.Day} {MonthAbbreviations[date.Month - 1]} {date.Year}";

    /// <summary>
    /// <c>--op add</c>/<c>sub</c>/<c>diff</c>: parse one exact Gregorian
    /// day-month-year date (no dual-dated year) into a <see cref="DateTime"/>.
    /// Throws <see cref="FormatException"/> for anything outside that
    /// grammar, including a day/month/year combination the Gregorian
    /// calendar has no date for (e.g. 30 FEB).
    /// </summary>
    public static DateTime ParseExactGregorianDate(string input)
    {
        if (!TryParseExactDayMonthYear(input, allowDualYear: false, out int day, out int month, out int year, out string? error))
            throw new FormatException(error);
        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new FormatException($"'{input}' is not a valid calendar date.");
        }
    }

    // -----------------------------------------------------------------------
    // date-calc arithmetic: pure functions of the DateTime/GedAge values a
    // caller supplies directly -- no document, no GEDCOM string in
    // or out. The exact DateTime these operate on is a local computational
    // value; it never replaces the raw GEDCOM DATE payload anywhere a
    // document is read, written, or presented.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve the right-hand (New Style) year of a dual-dated Gregorian
    /// year string ("1691/2" -> 1692, "1745/46" -> 1746): the right-hand
    /// digits replace the same number of trailing digits of the left-hand
    /// year, whatever the right-hand suffix's length -- this performs no
    /// Julian-to-Gregorian day offset. Returns the value unchanged when no
    /// dual year is present.
    /// </summary>
    public static int ResolveDualYear(string year)
    {
        ArgumentNullException.ThrowIfNull(year);
        int slash = year.IndexOf('/');
        if (slash < 0)
        {
            if (!int.TryParse(year, out int plain))
                throw new FormatException($"Not a valid year: '{year}'.");
            return plain;
        }

        string left = year[..slash];
        string right = year[(slash + 1)..];
        if (left.Length == 0 || right.Length == 0 || right.Length > left.Length ||
            !left.All(char.IsAsciiDigit) || !right.All(char.IsAsciiDigit))
        {
            throw new FormatException($"Not a valid dual-dated year: '{year}'.");
        }

        string resolved = left[..(left.Length - right.Length)] + right;
        if (!int.TryParse(resolved, out int iresolved))
            throw new FormatException($"Not a valid dual-dated year: '{year}'.");
        return iresolved;
    }

    /// <summary>
    /// date + age: years, then months, then days, each applied to the
    /// result of the step before it. Years and months clamp to the target
    /// month's last valid day (<see cref="DateTime.AddYears(int)"/> /
    /// <see cref="DateTime.AddMonths(int)"/>); the final day step crosses
    /// month/year boundaries normally (<see cref="DateTime.AddDays(double)"/>).
    /// Throws <see cref="ArgumentOutOfRangeException"/> if any step would
    /// leave the year 1-9999 range.
    /// </summary>
    public static DateTime AddAge(DateTime date, GedAge age) =>
        date.AddYears(age.Years).AddMonths(age.Months).AddDays(age.Days);

    /// <summary>
    /// date - age: the same year/month/day order as <see cref="AddAge"/>,
    /// each component negated.
    /// </summary>
    public static DateTime SubtractAge(DateTime date, GedAge age) =>
        date.AddYears(-age.Years).AddMonths(-age.Months).AddDays(-age.Days);

    /// <summary>
    /// Elapsed years/months/days between two dates via the same greedy
    /// calendar breakdown a gravestone's own inscribed age represents: the
    /// most whole years that fit, then the most whole months that fit in
    /// what's left, then the remaining days. Guarantees
    /// <c>AddAge(from, Diff(from, to)) == to</c>. Throws
    /// <see cref="ArgumentException"/> if <paramref name="from"/> is after
    /// <paramref name="to"/> -- elapsed time has no caller-invented sign
    /// convention here.
    /// </summary>
    public static GedAge Diff(DateTime from, DateTime to)
    {
        if (from > to)
            throw new ArgumentException("'from' must not be after 'to'.", nameof(from));

        int years = to.Year - from.Year;
        var afterYears = from.AddYears(years);
        if (afterYears > to) { years--; afterYears = from.AddYears(years); }

        int months = 0;
        while (true)
        {
            var next = afterYears.AddMonths(months + 1);
            if (next > to) break;
            months++;
        }
        var afterMonths = afterYears.AddMonths(months);

        int days = (to - afterMonths).Days;
        return new GedAge(years, months, days);
    }
}

/// <summary>Which DateValue grammar production a parsed <see cref="GedDateValue"/> matched.</summary>
public enum GedDateKind { Exact, About, Calculated, Estimated, Before, After, Between, Period }

/// <summary>
/// A parsed GEDCOM 7 DateValue. <see cref="From"/>/<see cref="To"/> are the
/// DateTime the primary/range-start and range-end resolve to, when they can
/// (null for a BCE date, a non-Gregorian-calendar date, or an unrecognized
/// month token). <see cref="FromYear"/>/<see cref="ToYear"/> are 0 when
/// unknown and otherwise survive even a partial date that leaves the
/// DateTime null — including a BCE year, which they hold negated.
/// </summary>
public sealed record GedDateValue(
    GedDateKind Kind,
    DateTime? From,
    DateTime? To,
    int FromYear,
    int ToYear);

/// <summary>
/// A genealogical age/duration in years, months, and days -- the same
/// notation an inscribed gravestone age or <c>date-calc --age</c> uses
/// ("63y 4m 2d"). Immutable value type; <see cref="ToString"/> always
/// prints all three canonical components with no leading zeroes, including
/// "0y 0m 0d" for a zero duration.
/// </summary>
public readonly record struct GedAge(int Years, int Months, int Days)
{
    public override string ToString() => $"{Years}y {Months}m {Days}d";

    /// <summary>
    /// Parse the <c>--age</c> grammar: one or more of "&lt;n&gt;y",
    /// "&lt;n&gt;m", "&lt;n&gt;d" in that order, separated by one ASCII
    /// space. Each component is optional, but present components must appear in y, m, d
    /// order with no repeats; each value is an unsigned base-10 integer with
    /// leading zeroes accepted. Throws <see cref="FormatException"/> for
    /// anything else, and <see cref="ArgumentOutOfRangeException"/> when
    /// months exceed 11 or days exceed 30 (the canonical remainder ranges
    /// <see cref="GedDate.Diff"/> produces) or years are negative.
    /// </summary>
    public static GedAge Parse(string text)
    {
        if (!TryParse(text, out var age, out string? error))
            throw new FormatException(error);
        return age;
    }

    public static bool TryParse(string? text, out GedAge age)
        => TryParse(text, out age, out _);

    static bool TryParse(string? text, out GedAge age, out string? error)
    {
        age = default;
        if (string.IsNullOrEmpty(text))
        {
            error = "age must not be blank.";
            return false;
        }

        string[] parts = text.Split(' ');
        if (parts.Any(p => p.Length == 0))
        {
            error = $"Invalid age '{text}': components must be separated by exactly one space, with no leading, trailing, or repeated spaces.";
            return false;
        }

        int? years = null, months = null, days = null;
        int lastUnit = -1; // 0=y, 1=m, 2=d

        foreach (string part in parts)
        {
            char unitChar = part[^1];
            int unitOrder = unitChar switch { 'y' => 0, 'm' => 1, 'd' => 2, _ => -1 };
            if (unitOrder < 0)
            {
                error = $"Invalid age component '{part}': expected an unsigned integer followed by 'y', 'm', or 'd'.";
                return false;
            }
            string digits = part[..^1];
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            {
                error = $"Invalid age component '{part}': expected an unsigned integer followed by 'y', 'm', or 'd'.";
                return false;
            }
            if (unitOrder <= lastUnit)
            {
                error = $"Invalid age '{text}': components must appear in y, m, d order with no repeats.";
                return false;
            }
            lastUnit = unitOrder;

            if (!int.TryParse(digits, out int value))
            {
                error = $"Invalid age component '{part}': value out of range.";
                return false;
            }

            switch (unitOrder)
            {
                case 0: years = value; break;
                case 1: months = value; break;
                case 2: days = value; break;
            }
        }

        if (months is > 11)
        {
            error = $"Invalid age '{text}': months must be 0-11, got {months}.";
            return false;
        }
        if (days is > 30)
        {
            error = $"Invalid age '{text}': days must be 0-30, got {days}.";
            return false;
        }

        age = new GedAge(years ?? 0, months ?? 0, days ?? 0);
        error = null;
        return true;
    }
}

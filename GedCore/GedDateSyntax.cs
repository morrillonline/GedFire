namespace GedCore;

/// <summary>
/// Strict GEDCOM 7 DateValue syntax: a date, an ABT/CAL/EST-approximated date, a BEF/AFT/BET … AND … range, or a
/// FROM … [TO …] / TO … period, each date optionally calendar-prefixed and BCE-suffixed. Unlike <see cref="GedDate"/>,
/// which reads whatever year it can find, this refuses anything a conformant file could not hold.
/// </summary>
public static class GedDateSyntax
{
    static readonly string[] Gregorian = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    static readonly string[] French = ["VEND", "BRUM", "FRIM", "NIVO", "PLUV", "VENT", "GERM", "FLOR", "PRAI", "MESS", "THER", "FRUC", "COMP"];
    static readonly string[] Hebrew = ["TSH", "CSH", "KSL", "TVT", "SHV", "ADR", "ADS", "NSN", "IYR", "SVN", "TMZ", "AAV", "ELL"];

    /// <summary>Whether <paramref name="value"/>, compared without regard to case, is a GEDCOM 7 DateValue.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value != value.Trim()) return false;
        string[] tokens = value.ToUpperInvariant().Split(' ');
        if (tokens.Any(t => t.Length == 0)) return false;

        return tokens[0] switch
        {
            "BET" => Split(tokens, "AND") is { } range && IsDate(range.First) && IsDate(range.Second),
            "FROM" => Split(tokens, "TO") is { } period ? IsDate(period.First) && IsDate(period.Second) : IsDate(tokens[1..]),
            "TO" or "BEF" or "AFT" or "ABT" or "CAL" or "EST" => IsDate(tokens[1..]),
            _ => IsDate(tokens),
        };
    }

    /// <summary>The refusal for a <paramref name="field"/> whose value is not a GEDCOM 7 date, or null when it is one.</summary>
    public static string? Check(string field, string? value) => IsValid(value) ? null
        : $"{field} \"{value}\" is not a GEDCOM date; write e.g. \"1 JUN 1840\", \"JUN 1840\", \"1840\", \"ABT 1840\", \"BEF 1840\", \"BET 1840 AND 1845\" or \"FROM 1840 TO 1845\".";

    // The tokens after the keyword split at the second keyword; null when it does not appear.
    static (string[] First, string[] Second)? Split(string[] tokens, string keyword)
    {
        int at = Array.IndexOf(tokens, keyword, 1);
        return at < 0 ? null : (tokens[1..at], tokens[(at + 1)..]);
    }

    static bool IsDate(string[] tokens)
    {
        if (tokens.Length == 0) return false;
        string calendar = "GREGORIAN";
        if (IsCalendar(tokens[0]))
        {
            calendar = tokens[0];
            tokens = tokens[1..];
        }
        if (tokens.Length > 0 && tokens[^1] == "BCE")
        {
            if (calendar is not ("GREGORIAN" or "JULIAN")) return false;
            tokens = tokens[..^1];
        }
        if (tokens.Length is 0 or > 3 || !IsNumber(tokens[^1], out int year)) return false;
        if (tokens.Length == 1) return true;
        if (!IsMonth(calendar, tokens[^2], out int month)) return false;
        if (tokens.Length == 2) return true;
        return IsNumber(tokens[0], out int day) && day >= 1 && day <= MaxDay(calendar, month, year);
    }

    static bool IsCalendar(string token) =>
        token is "GREGORIAN" or "JULIAN" or "FRENCH_R" or "HEBREW" || IsExtension(token);

    static bool IsMonth(string calendar, string token, out int month)
    {
        string[] months = calendar switch
        {
            "GREGORIAN" or "JULIAN" => Gregorian,
            "FRENCH_R" => French,
            "HEBREW" => Hebrew,
            _ => [],
        };
        month = Array.IndexOf(months, token) + 1;
        return month > 0 || IsExtension(token);
    }

    // An unknown month (an extension tag, or another calendar) can be checked only against the longest month.
    static int MaxDay(string calendar, int month, int year) =>
        calendar is "GREGORIAN" or "JULIAN" && month > 0 && year is >= 1 and <= 9999 ? DateTime.DaysInMonth(year, month)
        : calendar == "FRENCH_R" && month == 13 ? 6
        : calendar == "FRENCH_R" ? 30
        : 31;

    static bool IsExtension(string token) =>
        token.Length > 1 && token[0] == '_' && token.Skip(1).All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');

    static bool IsNumber(string token, out int value)
    {
        value = 0;
        return token.Length is > 0 and <= 9 && token.All(char.IsAsciiDigit) && int.TryParse(token, out value);
    }
}

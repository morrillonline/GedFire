using System.Text;

namespace GedFire.Gen;

// ---------------------------------------------------------------------------
// Genealogy model — version-agnostic, built from a parsed GedDocument.
// Mirrors the VB GedParser object graph used by the original Gedfire.
// ---------------------------------------------------------------------------

public sealed class GedSourceRecord
{
    public string Xref        { get; init; } = "";
    public string Author      { get; set; } = "";
    public string Title       { get; set; } = "";
    public string Publication { get; set; } = "";
    // Raw NOTE FullValue() from the GEDCOM — contains embedded pipe-delimited
    // properties (SHORTCITATION, NOCITATION, INLINE) that each inline ref
    // extracts when it resolves the pointer.
    public string NoteRaw     { get; set; } = "";
}

// One inline "2 SOUR @Sxxx@" reference, resolved against its global source.
public sealed class GedSourceRef
{
    public GedSourceRecord? GlobalSource  { get; set; }
    // Fields copied from the global source (and further parsed):
    public string Author      { get; set; } = "";
    public string Title       { get; set; } = "";
    public string Publication { get; set; } = "";
    public string Note        { get; set; } = "";  // note AFTER ParseSourceNote
    public string ShortCitation { get; set; } = "";
    public bool   IsNote  { get; set; }
    public bool   NoCitation    { get; set; }
    // From the inline reference itself:
    public string Page     { get; set; } = "";
    public string DataText { get; set; } = "";  // 3 DATA / 4 TEXT, after ParsePropertyList
}

/// <summary>One person-level biographical note, with any citations attached to that prose.</summary>
public sealed class GedNarrativeNote
{
    public string Text { get; set; } = "";
    // No MIME tag is GEDCOM's conventional text/plain default.
    public string? Mime { get; set; }
    public List<GedSourceRef> Sources { get; } = [];
}

public sealed class GedEvent
{
    public string            Tag     { get; init; } = "";
    public string            Date    { get; set; } = "";   // raw GEDCOM date string
    public string            Place   { get; set; } = "";
    public List<GedSourceRef> Sources { get; } = [];
    public List<GedMediaLink> Media   { get; } = [];
}

// One file within a media object (a record may carry several, e.g. multiple
// scans of the same document). Path is the raw FILE payload — a URL or a
// percent-escaped relative path.
public sealed record GedMediaFile(string Path, string MediaType, string? Medium, string? Title);

// A record-level multimedia object ("0 @M1@ OBJE"), addressed by pointer from
// INDI/FAM/event OBJE links.
public sealed class GedMediaObject
{
    public string Xref { get; init; } = "";
    public List<GedMediaFile> Files { get; } = [];
    public string? Title { get; set; }        // record-level TITL
}

// A CROP region (pixels of the source image); any/all sides may be absent.
public sealed record GedCrop(int? Top, int? Left, int? Height, int? Width);

// One "OBJE @M…@" link from an INDI/FAM/event to a media object. Title
// overrides the target's own title for display; Crop is link-specific.
public sealed record GedMediaLink(GedMediaObject Target, string? Title, GedCrop? Crop)
{
    // The title to display for this link: the link's own override, else the
    // target record's TITL, else the first file's TITL.
    public string? DisplayTitle => Title ?? Target.Title ?? Target.Files.FirstOrDefault()?.Title;
}


public sealed class GedIndividual
{
    public const string UnknownString = "____";

    public string Xref        { get; init; } = "";
    public string FirstName   { get; set; } = "";
    public string MiddleName  { get; set; } = "";
    public string LastNameRaw { get; set; } = "";
    public string Title       { get; set; } = "";
    public bool   IsMale      { get; set; }
    public string Fullname    { get; set; } = "";
    // Person-level NOTE/SNOTE structures in GEDCOM document order. GEDCOM
    // does not give a note a structured date, so source order is the reliable
    // authored order for a multi-part biography.
    public List<GedNarrativeNote> NarrativeNotes { get; } = [];
    // Raw normalized RESN payload (e.g. "CONFIDENTIAL, LOCKED"), or null if absent.
    public string? Restriction { get; set; }

    public GedEvent? Birth   { get; set; }
    public GedEvent? Death   { get; set; }
    public GedEvent? Will    { get; set; }
    public GedEvent? Probate { get; set; }
    public List<GedEvent>     Census     { get; } = [];
    public List<GedSourceRef> NameSources { get; } = [];

    public GedFamily?       FamChild  { get; set; }
    public List<GedFamily>  FamSpouse { get; } = [];

    // OBJE links in document order; the first is this person's portrait
    // ('preferred first' rule).
    public List<GedMediaLink> Media { get; } = [];

    // -----------------------------------------------------------------------
    // Computed display properties (mirror VB Individual members)
    // -----------------------------------------------------------------------

    public string LastName =>
        string.Equals(LastNameRaw, "unknown", StringComparison.OrdinalIgnoreCase)
            ? UnknownString : LastNameRaw;

    public string FirstMiddle()
    {
        string s = FirstName;
        if (MiddleName.Length > 0) s += " " + MiddleName;
        return string.Equals(s, "unknown", StringComparison.OrdinalIgnoreCase)
            ? UnknownString : s;
    }

    public string Husbandname()
    {
        string s = FirstMiddle() + " " + LastName;
        return Title.Length > 0 ? Title + " " + s : s;
    }

    // Returns the display name as wife, factoring in previous marriages.
    // husband == null means no specific husband context (plain name).
    public string Wifename(GedIndividual? husband)
    {
        if (IsMale) return "";
        string fm = FirstMiddle();
        string ln = LastName;

        foreach (var fam in FamSpouse)
        {
            var other = fam.SpouseOf(this);
            if (other == husband) break;
            if (ln.Length > 0) fm = fm + " (" + ln + ")";
            ln = other?.LastName ?? "";
        }
        string s = fm + " " + ln;
        return Title.Length > 0 ? Title + " " + s : s;
    }

    public bool IsChildless() => FamSpouse.All(f => f.Children.Count == 0);

    public bool HasNoEvents() =>
        Birth == null && Death == null && Will == null && Probate == null
        && FamSpouse.Count < 2;

    public IEnumerable<GedEvent> GetEvents()
    {
        if (Birth != null) yield return Birth;
        foreach (var c in Census.OrderBy(e => GedDate.ParseYear(e.Date)))
            yield return c;
        if (Death != null) yield return Death;
        if (Will != null) yield return Will;
        if (Probate != null) yield return Probate;
    }
}

public sealed class GedFamily
{
    public string Xref { get; init; } = "";
    public GedIndividual? Husband  { get; set; }
    public GedIndividual? Wife     { get; set; }
    public List<GedIndividual> Children { get; } = [];
    public GedEvent? Marriage { get; set; }
    public List<GedMediaLink> Media { get; } = [];

    public GedIndividual? SpouseOf(GedIndividual x)
    {
        if (x == Husband) return Wife;
        if (x == Wife) return Husband;
        return null;
    }
}

public sealed class GedModel
{
    public Dictionary<string, GedIndividual>  Individuals { get; } = new();
    public Dictionary<string, GedFamily>      Families    { get; } = new();
    public Dictionary<string, GedSourceRecord> Sources    { get; } = new();
    public Dictionary<string, GedMediaObject>  Media      { get; } = new();
    // GedcomFamilies preserves GEDCOM record order — used for URL assignment so
    // the first child-producing marriage for a husband gets the shorter URL,
    // matching the original Gedfire VB behavior (VB Collection iterates in add order).
    public List<GedFamily> GedcomFamilies { get; } = [];
    // Sorted by LastName, FirstMiddle, Birth for the index:
    public List<GedIndividual> SortedIndividuals { get; internal set; } = [];
}

// ---------------------------------------------------------------------------
// Small helpers
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

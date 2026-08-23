using System.Text;
using GedCore;

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
    // Whether a SEX record was actually present (any value, not just "M").
    // IsMale alone cannot distinguish "recorded female" from "sex never
    // stated" — both leave it false. Matching's nickname-map selection
    // needs that distinction, so it is tracked here rather than guessed.
    public bool   SexRecorded { get; set; }
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

// GedDate/GedDateValue/GedDateKind live in GedCore; see GedCore/GedDate.cs.
// GedIndividual.GetEvents() above uses GedDate.ParseYear via the
// "using GedCore;" at the top of this file.

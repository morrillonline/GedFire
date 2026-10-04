using GedCore;
using GedCore.Apply;
using GedFire.Gen;
using GedFire.Match;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Maps one xref in a GedModel to the record shape get_record and get_records
// return: person, family, source, or not_found. Pure mapping with no matching
// or scoring logic; media paths resolve against the directory it is built with.
// ---------------------------------------------------------------------------

public sealed class RecordMapper
{
    readonly string _mediaDir;

    public RecordMapper(string mediaDir)
    {
        if (string.IsNullOrEmpty(mediaDir)) throw new ArgumentException("Media directory must not be empty.", nameof(mediaDir));
        _mediaDir = mediaDir;
    }

    /// <summary>
    /// The record for one xref. A source looked up directly carries CitedBy;
    /// a person or family carries the full text of every source it cites when
    /// <paramref name="includeSources"/> is set.
    /// </summary>
    public object Map(GedModel model, string xref, bool includeSources = false)
    {
        if (model.Individuals.TryGetValue(xref, out var indi))
        {
            var person = MapPerson(indi);
            return includeSources ? person with { Sources = SourcesCitedBy(model, [person]) } : person;
        }
        if (model.Families.TryGetValue(xref, out var fam))
        {
            var family = MapFamily(fam);
            return includeSources ? family with { Sources = SourcesCitedBy(model, [family]) } : family;
        }
        if (model.Sources.TryGetValue(xref, out var src))
            return MapSource(src) with { CitedBy = CitedBy(model, xref) };
        return new NotFoundRecord("not_found", xref);
    }

    /// <summary>Each source cited anywhere on the given person and family records, once, in first-cited order.</summary>
    public IReadOnlyList<SourceRecord> SourcesCitedBy(GedModel model, IEnumerable<object> records) =>
        [.. records.SelectMany(CitationsOn)
            .Select(c => c.Source)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(model.Sources.ContainsKey)
            .Select(xref => MapSource(model.Sources[xref]))];

    static IEnumerable<CitationDetail> CitationsOn(object record) => record switch
    {
        PersonRecord p => p.NameCitations
            .Concat(EventCitations(p.Birth)).Concat(EventCitations(p.Death))
            .Concat(EventCitations(p.Will)).Concat(EventCitations(p.Probate))
            .Concat(p.Census.SelectMany(EventCitations))
            .Concat(p.OtherEvents.SelectMany(e => e.Citations))
            .Concat(p.Notes.SelectMany(n => n.Citations))
            .Concat(p.FamiliesAsSpouse.SelectMany(f => EventCitations(f.Marriage))),
        FamilyRecord f => EventCitations(f.Marriage),
        _ => [],
    };

    static IEnumerable<CitationDetail> EventCitations(EventDetail? ev) => ev?.Citations ?? [];

    static List<CitedByEntry> CitedBy(GedModel model, string sourceXref)
    {
        var entries = new List<CitedByEntry>();
        void Add(string xref, string recordType, string field, IEnumerable<GedSourceRef> refs)
        {
            if (refs.Any(r => r.GlobalSource?.Xref == sourceXref) &&
                !entries.Any(e => e.Xref == xref && e.Field == field))
                entries.Add(new CitedByEntry(xref, recordType, field));
        }

        foreach (var indi in model.Individuals.Values)
        {
            Add(indi.Xref, "person", "name", indi.NameSources);
            Add(indi.Xref, "person", "birth", indi.Birth?.Sources ?? []);
            Add(indi.Xref, "person", "death", indi.Death?.Sources ?? []);
            Add(indi.Xref, "person", "will", indi.Will?.Sources ?? []);
            Add(indi.Xref, "person", "probate", indi.Probate?.Sources ?? []);
            Add(indi.Xref, "person", "census", indi.Census.SelectMany(c => c.Sources));
            Add(indi.Xref, "person", "otherEvent", indi.OtherEvents.SelectMany(e => e.Sources));
            Add(indi.Xref, "person", "note", indi.NarrativeNotes.SelectMany(n => n.Sources));
        }
        foreach (var fam in model.Families.Values)
            Add(fam.Xref, "family", "marriage", fam.Marriage?.Sources ?? []);
        return entries;
    }

    // -------------------------------------------------------------------
    // Person mapping
    // -------------------------------------------------------------------

    PersonRecord MapPerson(GedIndividual indi) => new(
        "person",
        indi.Xref,
        PersonDisplay.FullName(indi),
        OrNull(indi.Title),
        indi.SexRecorded ? (indi.IsMale ? "M" : "F") : null,
        MapEvent(indi.Birth),
        MapEvent(indi.Death),
        MapEvent(indi.Will),
        MapEvent(indi.Probate),
        [.. indi.Census.Select(ev => MapEvent(ev)!)],
        [.. indi.OtherEvents.Select(MapOtherEvent)],
        MapCitations(indi.NameSources),
        [.. indi.NarrativeNotes.Select(MapNote)],
        indi.Restriction,
        MapMediaList(indi.Media),
        MapFamilyAsChild(indi.FamChild),
        [.. indi.FamSpouse.Select(f => MapSpouseFamily(f, indi))]);

    static ParentFamilyReference? MapFamilyAsChild(GedFamily? famChild)
    {
        if (famChild is null) return null;
        return new ParentFamilyReference(
            famChild.Xref,
            famChild.Husband != null ? PersonDisplay.FullName(famChild.Husband) : null,
            famChild.Wife != null ? PersonDisplay.FullName(famChild.Wife) : null);
    }

    SpouseFamilyDetail MapSpouseFamily(GedFamily fam, GedIndividual owner)
    {
        var spouse = fam.SpouseOf(owner);
        return new SpouseFamilyDetail(
            fam.Xref,
            spouse != null ? PersonDisplay.FullName(spouse) : null,
            MapEvent(fam.Marriage),
            [.. fam.Children.Select(MapChild)]);
    }

    // -------------------------------------------------------------------
    // Family mapping
    // -------------------------------------------------------------------

    FamilyRecord MapFamily(GedFamily fam) => new(
        "family",
        fam.Xref,
        MapSpouseReference(fam.Husband),
        MapSpouseReference(fam.Wife),
        MapEvent(fam.Marriage),
        [.. fam.Children.Select(MapChild)],
        MapMediaList(fam.Media));

    static SpouseReference? MapSpouseReference(GedIndividual? indi) =>
        indi is null ? null : new SpouseReference(indi.Xref, PersonDisplay.FullName(indi));

    static ChildIdentity MapChild(GedIndividual child) => new(
        child.Xref,
        PersonDisplay.FullName(child),
        child.Birth is null ? null : NullIfZero(GedDate.ParseYear(child.Birth.Date)));

    // -------------------------------------------------------------------
    // Source mapping
    // -------------------------------------------------------------------

    static SourceRecord MapSource(GedSourceRecord src)
    {
        string note = FtmCitationText.ParseSourceNote(src.NoteRaw, out _, out _);
        return new SourceRecord(
            "source",
            src.Xref,
            OrNull(src.Author),
            OrNull(src.Title),
            OrNull(src.Publication),
            OrNull(note));
    }

    // -------------------------------------------------------------------
    // Shared: events, notes, media, citations
    // -------------------------------------------------------------------

    EventDetail? MapEvent(GedEvent? ev)
    {
        if (ev is null) return null;
        return new EventDetail(
            OrNull(ev.Date),
            NullIfZero(GedDate.ParseYear(ev.Date)),
            GedDate.Qualifier(ev.Date),
            OrNull(ev.Place),
            MapCitations(ev.Sources),
            MapMediaList(ev.Media));
    }

    OtherEventDetail MapOtherEvent(GedEvent ev) => new(
        ev.Tag,
        OrNull(ev.Date),
        NullIfZero(GedDate.ParseYear(ev.Date)),
        GedDate.Qualifier(ev.Date),
        OrNull(ev.Place),
        MapCitations(ev.Sources),
        MapMediaList(ev.Media));

    static NoteDetail MapNote(GedNarrativeNote note) =>
        new(note.Text, note.Mime, MapCitations(note.Sources));

    // A citation naming a source record that does not exist is dropped; an
    // inline note is kept even when it names no source.
    static List<CitationDetail> MapCitations(IEnumerable<GedSourceRef> sourceRefs) =>
        [.. sourceRefs
            .Where(s => s.GlobalSource != null || s.IsNote)
            .Select(s => new CitationDetail(
                s.NoCitation ? "personalNote" : s.IsNote ? "inlineNote" : "citation",
                s.GlobalSource?.Xref,
                OrNull(s.Page),
                OrNull(s.DataText),
                s.Quay))];

    List<MediaDetail> MapMediaList(IEnumerable<GedMediaLink> links) =>
        [.. links.Select(MapMedia)];

    MediaDetail MapMedia(GedMediaLink link) => new(
        link.Target.Xref,
        OrNull(link.DisplayTitle),
        MapCrop(link.Crop),
        [.. link.Target.Files.Select(MapMediaFile)]);

    // Resolves a raw GEDCOM FILE payload the same way SiteGenerator's
    // ResolveMediaSrc does for HTML generation: an absolute URL passes
    // through unchanged; a relative path becomes an
    // absolute local path when it resolves to an existing file under
    // _mediaDir without escaping it; anything else keeps the raw payload,
    // flagged unresolved rather than left looking usable.
    MediaFileDetail MapMediaFile(GedMediaFile file)
    {
        var (path, resolved) = ResolveMediaPath(file.Path);
        return new MediaFileDetail(path, file.MediaType, OrNull(file.Medium), OrNull(file.Title), resolved);
    }

    (string Path, bool Resolved) ResolveMediaPath(string rawPath)
    {
        if (MediaPaths.IsAbsoluteUrl(rawPath))
            return (rawPath, true);

        string relative = MediaPaths.UnescapeFilePath(rawPath);
        string mediaRoot = Path.GetFullPath(_mediaDir);
        string full = Path.GetFullPath(Path.Combine(mediaRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        bool withinRoot = full == mediaRoot || full.StartsWith(mediaRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        return withinRoot && File.Exists(full) ? (full, true) : (rawPath, false);
    }

    static CropDetail? MapCrop(GedCrop? crop) =>
        crop is null ? null : new CropDetail(crop.Top, crop.Left, crop.Height, crop.Width);

    static string? OrNull(string? s) => string.IsNullOrEmpty(s) ? null : s;

    static int? NullIfZero(int year) => year != 0 ? year : null;
}

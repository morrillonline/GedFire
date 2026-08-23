using GedCore;

namespace GedFire.Gen;

// ---------------------------------------------------------------------------
// Builds a GedModel from a parsed GedDocument.
// ---------------------------------------------------------------------------

public static class ModelBuilder
{
    public static GedModel Build(GedDocument doc) => new BuildContext().Execute(doc);

    // Sort individuals for the index. VB uses String.Compare(x,y,False) =
    // case-sensitive InvariantCulture, where "_" sorts before letters (symbols
    // precede A-Z in Unicode collation), matching the original index. Also
    // called by PrivacyFilter after placeholder names change sort keys.
    internal static void SortForIndex(GedModel model)
    {
        model.SortedIndividuals = [.. model.Individuals.Values
            .OrderBy(i => i.LastName,      StringComparer.InvariantCulture)
            .ThenBy(i => i.FirstMiddle(),  StringComparer.InvariantCulture)
            .ThenBy(i => GedDate.ParseYear(i.Birth?.Date))];
    }

    // -----------------------------------------------------------------------
    // BuildContext — instance encapsulates all mutable state for one Build()
    // call, so concurrent/sequential calls can never interfere.
    // -----------------------------------------------------------------------

    private sealed class BuildContext
    {
        // Source-ref resolution: maps each GedSourceRef to its GEDCOM xref
        // and any inline NOTE override.  Populated during Pass 1, consumed
        // and discarded at the end of Pass 2.
        readonly Dictionary<GedSourceRef, string> _pending      = new();
        readonly Dictionary<GedSourceRef, string> _inlineNotes  = new();

        // Xref scaffolding collected during Pass 1 for use in Pass 2.
        // Keeping this here means domain objects carry only resolved
        // object-graph references — no build-time artifacts.
        readonly Dictionary<GedIndividual, string>       _indiParentXref  = new();
        readonly Dictionary<GedIndividual, List<string>> _indiSpouseXrefs = new();

        // Shared-note resolution: texts of level-0 NOTE (5.5) / SNOTE (7.0)
        // records, and the individuals whose note is a pointer to one.
        readonly Dictionary<string, GedNarrativeNote> _sharedNotes = new();
        readonly Dictionary<GedNarrativeNote, string> _notePointers = new();
        readonly Dictionary<GedFamily, string>           _famHusbXref     = new();
        readonly Dictionary<GedFamily, string>           _famWifeXref     = new();
        readonly Dictionary<GedFamily, List<string>>     _famChildXrefs   = new();

        // Media links: target xref + link-specific overrides, keyed by owner
        // and resolved against model.Media in Pass 2 (an OBJE record may
        // appear anywhere relative to the record that links to it).
        readonly record struct PendingMediaLink(string Xref, string? Title, GedCrop? Crop);
        readonly Dictionary<GedIndividual, List<PendingMediaLink>> _indiMediaLinks = new();
        readonly Dictionary<GedFamily, List<PendingMediaLink>>     _famMediaLinks  = new();
        readonly Dictionary<GedEvent, List<PendingMediaLink>>      _eventMediaLinks = new();

        public GedModel Execute(GedDocument doc)
        {
            var model = new GedModel();

            // Pass 1: create objects from level-0 records.
            foreach (var rec in doc.Records)
            {
                switch (rec.Tag)
                {
                    case "INDI":
                        var indi = ParseIndi(rec);
                        model.Individuals[indi.Xref] = indi;
                        break;
                    case "FAM":
                        var fam = ParseFam(rec);
                        model.Families[fam.Xref] = fam;
                        model.GedcomFamilies.Add(fam);  // preserve GEDCOM insertion order
                        break;
                    case "SOUR":
                        var src = ParseSource(rec);
                        model.Sources[src.Xref] = src;
                        break;
                    case "OBJE" when rec.Xref is not null:
                        var media = ParseMedia(rec);
                        model.Media[media.Xref] = media;
                        break;
                    case "NOTE" or "SNOTE" when rec.Xref is not null:
                        _sharedNotes[rec.Xref] = new GedNarrativeNote
                        {
                            Text = rec.FullValue(),
                            Mime = NoteMime(rec),
                        };
                        break;
                }
            }

            // Pass 2: resolve xrefs.
            ResolvePointers(model);

            // Pass 3: sort individuals for the index.
            SortForIndex(model);

            return model;
        }

        // -------------------------------------------------------------------
        // INDI parsing
        // -------------------------------------------------------------------

        GedIndividual ParseIndi(GedRecord rec)
        {
            var indi = new GedIndividual { Xref = rec.Xref ?? "" };
            string? parentXref = null;
            List<string>? spouseXrefs = null;

            foreach (var child in rec.Children)
            {
                switch (child.Tag)
                {
                    case "NAME":
                        SetFullname(indi, child.FullValue());
                        // Read the NAME's narrative children in document order so a
                        // migrated inline note (a 2 NOTE) keeps its position among the
                        // name's SOUR citations instead of being appended after them.
                        foreach (var sub in child.Children)
                        {
                            if (sub.Tag == "SOUR")
                                indi.NameSources.Add(BuildSourceRef(sub));
                            else if (sub.Tag is "NOTE" or "SNOTE")
                                AddNameNote(indi, sub);
                        }
                        break;
                    case "SEX":
                        indi.IsMale = child.Value.Equals("M", StringComparison.OrdinalIgnoreCase);
                        indi.SexRecorded = true;
                        if (string.Equals(indi.FirstMiddle(), "unknown", StringComparison.OrdinalIgnoreCase))
                            SetFullname(indi, indi.FirstMiddle() + " /" + indi.LastNameRaw + "/");
                        break;
                    case "TITL":
                        indi.Title = child.Value;
                        break;
                    case "RESN":
                        indi.Restriction = child.Value.Trim().ToUpperInvariant();
                        break;
                    case "BIRT":
                        indi.Birth = ParseEvent(child, "BIRT");
                        break;
                    case "DEAT":
                        indi.Death = ParseEvent(child, "DEAT");
                        break;
                    case "WILL":
                        indi.Will = ParseEvent(child, "WILL");
                        break;
                    case "PROB":
                        indi.Probate = ParseEvent(child, "PROB");
                        break;
                    case "CENS":
                    case "RESI":
                        indi.Census.Add(ParseEvent(child, child.Tag));
                        break;
                    case "FAMS":
                        if (child.Value.Length > 0)
                            (spouseXrefs ??= []).Add(child.Value);
                        break;
                    case "FAMC":
                        if (child.Value.Length > 0) parentXref = child.Value;
                        break;
                    case "OBJE":
                        AddMediaLink(child, _indiMediaLinks, indi);
                        break;
                    case "NOTE":
                    case "SNOTE":
                        ParseIndiNote(indi, child);
                        break;
                }
            }

            if (parentXref != null) _indiParentXref[indi] = parentXref;
            if (spouseXrefs != null) _indiSpouseXrefs[indi] = spouseXrefs;

            return indi;
        }

        // An INDI-level NOTE with SOUR citation(s) is GEDCOM 7's narrative
        // form (produced by Ged70Upgrader from the legacy "Inline: TRUE|"
        // citation text): the prose renders as a bio paragraph with the
        // citation footnoted. Every note stays in the person's ordered
        // narrative collection, whether cited or not.
        void ParseIndiNote(GedIndividual indi, GedRecord noteRec)
        {
            var citations = SourceRefsOf(noteRec).ToList();
            if (citations.Count == 0)
            {
                // Pointer-ness is decided on the raw on-disk payload: an
                // escaped text payload ("@@VOID@", "@@X@") un-escapes via
                // FullValue() into something pointer-shaped, but it is text.
                if (noteRec.IsVoidPointer)
                {
                    // @VOID@ means "no record"; skip the note entirely rather
                    // than surfacing the literal pointer as note text.
                    return;
                }
                if (IsXrefPointer(noteRec.Value))
                {
                    // Shared-note pointer (NOTE @X@ / SNOTE @X@) — resolved to
                    // the record's text in Pass 2.
                    var note = new GedNarrativeNote();
                    indi.NarrativeNotes.Add(note);
                    _notePointers[note] = noteRec.Value;
                    return;
                }
                indi.NarrativeNotes.Add(new GedNarrativeNote
                {
                    Text = noteRec.FullValue(),
                    Mime = NoteMime(noteRec),
                });
                return;
            }

            var narrative = new GedNarrativeNote
            {
                Text = noteRec.FullValue(),
                Mime = NoteMime(noteRec),
            };
            narrative.Sources.AddRange(citations);
            indi.NarrativeNotes.Add(narrative);
        }

        static string? NoteMime(GedRecord note) =>
            string.Equals(note.FirstChild("MIME")?.Value, "text/html", StringComparison.OrdinalIgnoreCase)
                ? "text/html"
                : null;

        // A NOTE substructure of NAME carrying SOUR citation(s) is a narrative
        // note positioned inline among the name's citations: its prose renders
        // as a bio paragraph (IsNote) with the citation footnoted; without a
        // citation it is bare prose. Mirrors the person-level ParseIndiNote but
        // never yields a person-level NarrativeNote (a name-level note is
        // always rendered with the name's source annotations).
        void AddNameNote(GedIndividual indi, GedRecord noteRec)
        {
            var citations = SourceRefsOf(noteRec).ToList();
            string prose = noteRec.FullValue();
            if (citations.Count == 0)
            {
                indi.NameSources.Add(new GedSourceRef { IsNote = true, DataText = prose });
                return;
            }
            foreach (var sref in citations)
            {
                sref.IsNote = true;
                sref.DataText = prose;
                prose = "";   // prose renders once; extra citations footnote only
                indi.NameSources.Add(sref);
            }
        }

        static bool IsXrefPointer(string s) =>
            s.Length > 2 && s[0] == '@' && s[^1] == '@'
            && s.IndexOf('@', 1) == s.Length - 1;

        static void SetFullname(GedIndividual indi, string nameValue)
        {
            // GedNamePayload.Split (GedCore) owns the slash tokenizing; the
            // no-slash fallback below (whole value as last name) is this
            // caller's own longstanding policy choice, unchanged.
            var (fm, ln) = GedNamePayload.Split(nameValue);
            if (ln is not null)
            {
                indi.LastNameRaw = ln;
                int sp = fm.IndexOf(' ');
                if (sp < 0) { indi.FirstName = fm; indi.MiddleName = ""; }
                else { indi.FirstName = fm[..sp].Trim(); indi.MiddleName = fm[(sp + 1)..].Trim(); }
                indi.Fullname = (fm.Length > 0 ? fm + " " : "") + indi.LastNameRaw;
            }
            else
            {
                indi.Fullname = nameValue;
                indi.LastNameRaw = nameValue;
            }
        }

        // Build a GedEvent from a BIRT/DEAT/MARR/CENS/WILL/PROB record.
        // Sources can live under the event directly OR under its DATE child.
        GedEvent ParseEvent(GedRecord rec, string tag)
        {
            var ev = new GedEvent { Tag = tag };
            foreach (var child in rec.Children)
            {
                switch (child.Tag)
                {
                    case "DATE":
                        ev.Date = child.FullValue();
                        // Sources under DATE are promoted to the event (mirrors VB PromoteSources)
                        foreach (var s in SourceRefsOf(child)) ev.Sources.Add(s);
                        break;
                    case "PLAC":
                        ev.Place = child.FullValue();
                        foreach (var s in SourceRefsOf(child)) ev.Sources.Add(s);
                        break;
                    case "SOUR":
                        ev.Sources.Add(BuildSourceRef(child));
                        break;
                    case "OBJE":
                        AddMediaLink(child, _eventMediaLinks, ev);
                        break;
                }
            }
            return ev;
        }

        // Collect all immediate SOUR children of a record as SourceRefs.
        IEnumerable<GedSourceRef> SourceRefsOf(GedRecord rec)
        {
            foreach (var child in rec.Children)
                if (child.Tag == "SOUR")
                    yield return BuildSourceRef(child);
        }

        // Build a GedSourceRef from a "2 SOUR @Sxxx@" (or same-level) record.
        GedSourceRef BuildSourceRef(GedRecord sourRec)
        {
            var sref = new GedSourceRef();
            sref.Page = sourRec.FirstChild("PAGE")?.Value ?? "";

            // DATA / TEXT — the inline annotation text
            var dataTxt = sourRec.FirstChild("DATA")?.FirstChild("TEXT")?.FullValue() ?? "";
            if (dataTxt.Length > 0)
            {
                dataTxt = FtmCitationText.ParsePropertyList(dataTxt,
                    out bool inl, out bool noCit, out string sc);
                sref.DataText = dataTxt;
                if (inl) sref.IsNote = true;
                if (noCit) sref.NoCitation = true;
                if (sc.Length > 0) sref.ShortCitation = sc;
            }

            // Stash xref for resolution in Pass 2
            sref.GlobalSource = null;
            _pending[sref] = sourRec.Value;  // "@Sxxx@"

            // Inline NOTE override — resolved in Pass 2
            var noteChild = sourRec.FirstChild("NOTE");
            if (noteChild != null)
            {
                var rawNote = noteChild.FullValue();
                string note2 = FtmCitationText.ParseSourceNote(rawNote,
                    out bool _, out string _);
                _inlineNotes[sref] = note2.Length > 0 ? note2 : rawNote;
            }

            return sref;
        }

        // -------------------------------------------------------------------
        // FAM parsing
        // -------------------------------------------------------------------

        GedFamily ParseFam(GedRecord rec)
        {
            var fam = new GedFamily { Xref = rec.Xref ?? "" };
            string? husbXref = null, wifeXref = null;
            List<string>? childXrefs = null;

            foreach (var child in rec.Children)
            {
                switch (child.Tag)
                {
                    case "HUSB":
                        if (child.Value != GedRecord.VoidPointer) husbXref = child.Value;
                        break;
                    case "WIFE":
                        if (child.Value != GedRecord.VoidPointer) wifeXref = child.Value;
                        break;
                    case "CHIL":
                        if (child.Value != GedRecord.VoidPointer)
                            (childXrefs ??= []).Add(child.Value);
                        break;
                    case "MARR":
                        fam.Marriage = ParseEvent(child, "MARR");
                        break;
                    case "OBJE":
                        AddMediaLink(child, _famMediaLinks, fam);
                        break;
                }
            }

            if (husbXref != null) _famHusbXref[fam] = husbXref;
            if (wifeXref != null) _famWifeXref[fam] = wifeXref;
            if (childXrefs != null) _famChildXrefs[fam] = childXrefs;

            return fam;
        }

        // -------------------------------------------------------------------
        // SOUR (global source) parsing
        // -------------------------------------------------------------------

        static GedSourceRecord ParseSource(GedRecord rec)
        {
            var src = new GedSourceRecord { Xref = rec.Xref ?? "" };
            foreach (var child in rec.Children)
            {
                switch (child.Tag)
                {
                    case "AUTH": src.Author      = child.Value; break;
                    case "TITL": src.Title       = child.FullValue(); break;
                    case "PUBL": src.Publication = child.FullValue(); break;
                    case "NOTE": src.NoteRaw     = child.FullValue(); break;
                }
            }
            return src;
        }

        // -------------------------------------------------------------------
        // OBJE (media object) parsing
        // -------------------------------------------------------------------

        static GedMediaObject ParseMedia(GedRecord rec)
        {
            var media = new GedMediaObject { Xref = rec.Xref ?? "" };
            foreach (var child in rec.Children)
            {
                switch (child.Tag)
                {
                    case "TITL":
                        media.Title = child.FullValue();
                        break;
                    case "FILE":
                        var form = child.FirstChild("FORM");
                        media.Files.Add(new GedMediaFile(
                            child.FullValue(),
                            form?.Value ?? "",
                            form?.FirstChild("MEDI")?.Value,
                            child.FirstChild("TITL")?.FullValue()));
                        break;
                }
            }
            return media;
        }

        // A record's "OBJE @M…@" child links it to a media object. Only
        // pointer payloads are handled (a 5.5-style inline OBJE is skipped —
        // the master is 7.0); resolution against model.Media happens in
        // Pass 2, since the target OBJE record may appear later in the file.
        static void AddMediaLink<TOwner>(
            GedRecord child,
            Dictionary<TOwner, List<PendingMediaLink>> pending,
            TOwner owner) where TOwner : notnull
        {
            if (!child.IsPointerValue) return;
            if (!pending.TryGetValue(owner, out var list))
                pending[owner] = list = [];
            list.Add(new PendingMediaLink(child.Value, child.FirstChild("TITL")?.FullValue(), ParseCrop(child.FirstChild("CROP"))));
        }

        static GedCrop? ParseCrop(GedRecord? cropRec)
        {
            if (cropRec == null) return null;
            return new GedCrop(
                ParseIntOrNull(cropRec.FirstChild("TOP")?.Value),
                ParseIntOrNull(cropRec.FirstChild("LEFT")?.Value),
                ParseIntOrNull(cropRec.FirstChild("HEIGHT")?.Value),
                ParseIntOrNull(cropRec.FirstChild("WIDTH")?.Value));
        }

        static int? ParseIntOrNull(string? s) => int.TryParse(s, out int v) ? v : null;

        // Resolve every owner's pending media links against model.Media,
        // preserving document order; a dangling or @VOID@ target simply
        // yields no link (consistent with Subproject B's dangling-pointer
        // handling elsewhere in this builder).
        static void ResolveMediaLinks<TOwner>(
            GedModel model,
            Dictionary<TOwner, List<PendingMediaLink>> pending,
            Func<TOwner, List<GedMediaLink>> mediaOf) where TOwner : notnull
        {
            foreach (var (owner, links) in pending)
            {
                var target = mediaOf(owner);
                foreach (var link in links)
                    if (model.Media.TryGetValue(link.Xref, out var mo))
                        target.Add(new GedMediaLink(mo, link.Title, link.Crop));
            }
        }

        // -------------------------------------------------------------------
        // Resolve pointers (pass 2)
        // -------------------------------------------------------------------

        void ResolvePointers(GedModel model)
        {
            // Resolve shared-note pointers to the note record's text; a
            // dangling pointer keeps its literal value (legacy behavior).
            foreach (var (note, xref) in _notePointers)
                if (_sharedNotes.TryGetValue(xref, out var shared))
                {
                    note.Text = shared.Text;
                    note.Mime = shared.Mime;
                }
                else
                    note.Text = xref;

            // Resolve individuals → families
            foreach (var indi in model.Individuals.Values)
            {
                if (_indiParentXref.TryGetValue(indi, out var parentXref) &&
                    model.Families.TryGetValue(parentXref, out var fc))
                    indi.FamChild = fc;

                if (_indiSpouseXrefs.TryGetValue(indi, out var spouseXrefs))
                    foreach (var xref in spouseXrefs)
                        if (model.Families.TryGetValue(xref, out var fs))
                            indi.FamSpouse.Add(fs);
            }

            // Resolve families → individuals
            foreach (var fam in model.Families.Values)
            {
                if (_famHusbXref.TryGetValue(fam, out var husbXref) &&
                    model.Individuals.TryGetValue(husbXref, out var h))
                    fam.Husband = h;

                if (_famWifeXref.TryGetValue(fam, out var wifeXref) &&
                    model.Individuals.TryGetValue(wifeXref, out var w))
                    fam.Wife = w;

                if (_famChildXrefs.TryGetValue(fam, out var childXrefs))
                    foreach (var xref in childXrefs)
                        if (model.Individuals.TryGetValue(xref, out var c))
                            fam.Children.Add(c);
            }

            // Resolve media links (INDI, FAM, and event OBJE)
            ResolveMediaLinks(model, _indiMediaLinks, i => i.Media);
            ResolveMediaLinks(model, _famMediaLinks, f => f.Media);
            ResolveMediaLinks(model, _eventMediaLinks, e => e.Media);

            // Resolve inline source references → global sources
            foreach (var (sref, xref) in _pending)
            {
                if (!model.Sources.TryGetValue(xref, out var gs)) continue;
                sref.GlobalSource = gs;
                sref.Author      = gs.Author;
                sref.Title       = gs.Title;
                sref.Publication = gs.Publication;

                // Extract directives from the global note. The FTM export puts
                // the pre-formatted citation on the first line with directive
                // lines after it, so this must NOT strip text greedily — the
                // note text is the citation base SourcePhrase renders.
                if (gs.NoteRaw.Length > 0)
                {
                    string note = FtmCitationText.ParseSourceNote(gs.NoteRaw,
                        out bool noCit, out string sc);
                    sref.Note          = note;
                    sref.ShortCitation = sc.Length > 0 ? sc : sref.ShortCitation;
                    // A NoCitation source is the "Personal note" pseudo-source (@S00257@):
                    // it carries research prose, not a bibliographic reference, so it is a
                    // NOTE — rendered as body text near the person, never a footnote.
                    if (noCit) { sref.NoCitation = true; sref.IsNote = true; }
                }

                // Let inline NOTE child override
                if (_inlineNotes.TryGetValue(sref, out var overrideNote))
                    sref.Note = overrideNote;
            }
            // _pending and _inlineNotes go out of scope with this BuildContext instance —
            // no manual Clear() call needed.
        }
    }
}

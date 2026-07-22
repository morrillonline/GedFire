namespace GedCore.Ged70;

/// <summary>
/// One-way, in-place upgrade of a parsed GEDCOM 5.5 document to native,
/// fully spec-conformant GEDCOM 7.0 structure.  Complements Ged70Formatter:
/// the formatter's rewrites (VERS, encoding, CONC → _CONC) are reversible so
/// 5.5 output can be reconstructed byte-for-byte; this upgrader applies the
/// transforms that are deliberately irreversible.
///
/// Transforms applied:
///
///   CONC records   → folded into the value of the line they continue (the
///                    owning record, or the most recent CONT when the
///                    continuation follows a line break).  GEDCOM 7.0 removed
///                    CONC and has no line-length limit.  CONT is kept — it
///                    remains the 7.0 representation of a literal line break.
///
///   HEAD.CHAR      → removed (deleted from the 7.0 spec; the UTF-8 BOM
///                    written by Ged70Formatter identifies the encoding).
///   HEAD.FILE      → removed (deleted from the 7.0 spec).
///   HEAD.DEST      → removed (deleted from the 7.0 spec).
///   HEAD.GEDC.FORM → removed (7.0's GEDC permits only VERS; the
///                    LINEAGE-LINKED form declaration is gone).
///   HEAD.SUBM      → removed when it points to a bare submitter record;
///                    the bare SUBM record itself is removed too (7.0
///                    requires SUBM.NAME, and a record with no substructures
///                    carries no information).
///   HEAD.SCHMA     → gains a TAG entry for every extension tag (starting
///                    with '_') used anywhere in the upgraded document.
///                    A surviving CONC node (none survive an upgrade run,
///                    which folds them all) would count as '_CONC', the tag
///                    Ged70Formatter writes it as. Existing declarations
///                    are left untouched.
///
///   NOTE records   → SNOTE records ("0 @X@ NOTE" is not valid 7.0), with
///                    every "NOTE @X@" pointer re-tagged to "SNOTE @X@".
///
///   Free-text source citations ("2 SOUR Details: …") → pointer citations.
///                    7.0 requires citation payloads to be pointers, so each
///                    distinct text becomes a new 0 SOUR record holding the
///                    text verbatim as its NOTE, and the citation points to
///                    it.  (Bonus: the generator renders a SOUR-record NOTE
///                    as a pre-formatted citation, so these previously
///                    invisible citations become footnotes.)
///
///   ALIA with a text payload → NICK under the individual's primary NAME.
///                    7.0 ALIA must point to an INDI record; FTM used it for
///                    nicknames, which is exactly what the NICK name piece
///                    is for.  Pointer ALIA is left alone.
///
///   Empty contact lines (ADDR/EMAIL/PHON/FAX/WWW with no payload and no
///                    substructures) → removed; 7.0 requires payloads there.
///
///   "Inline: TRUE|…" narrative citation text → NOTE structure.  The legacy
///                    data marks biography prose with an FTM/Gedfire
///                    property-list prefix inside NAME.SOUR.DATA.TEXT.
///                    GEDCOM 7 NOTE structures support source citations, so
///                    each such citation becomes a person-level NOTE holding
///                    the prose with a SOUR citation beneath it; PAGE and any
///                    extension records (e.g. _FOOT) move onto that citation.
///                    Only this marked form is converted.  Citations of the
///                    "Personal note" pseudo-source (@S00257@) are NOT
///                    converted even though their TEXT is also commentary:
///                    the INLINE marker in that source's record-level NOTE is
///                    inert (mid-text markers never parsed, on the legacy
///                    site or here), so those texts have always rendered as
///                    numbered footnotes — converting them to NOTEs would
///                    change page content.  Citation DATA.TEXT remains valid
///                    GEDCOM 7, so they stay as they are.
///
/// Folding CONC discards the original 5.5 line-split positions, so an
/// upgraded document can no longer round-trip to a byte-identical 5.5 file.
/// Intended for the one-way migration of a master file.
/// </summary>
public static class Ged70Upgrader
{
    public readonly record struct UpgradeSummary(
        int ConcLinesFolded,
        int HeaderRecordsRemoved,
        int InlineNotesConverted,
        int NoteRecordsConverted,
        int FreeTextCitationsConverted,
        int AliasesConverted,
        int EmptyContactLinesRemoved,
        int SubmitterRecordsRemoved,
        int SchemaTagsDeclared);

    /// <summary>Upgrade the document in place and report what changed.</summary>
    public static UpgradeSummary UpgradeInPlace(GedDocument doc) =>
        new UpgradeContext(doc).Run();

    // -----------------------------------------------------------------------
    // UpgradeContext — encapsulates all mutable state for one upgrade run.
    // -----------------------------------------------------------------------

    private sealed class UpgradeContext(GedDocument doc)
    {
        int _folded, _headerRemoved, _inlineConverted, _noteRecords,
            _freeTextCitations, _aliases, _emptyContacts, _submRemoved;
        GedRecord? _head;

        public UpgradeSummary Run()
        {
            // Bare submitter records (no substructures) are removed along
            // with the header pointers to them; identify them first.
            var bareSubmXrefs = doc.Records
                .Where(r => r.Tag == "SUBM" && r.Xref is not null && r.Children.Count == 0)
                .Select(r => r.Xref!)
                .ToHashSet();

            foreach (var root in doc.Records)
            {
                if (root.Tag == "HEAD")
                {
                    _head = root;
                    CleanHeader(root, bareSubmXrefs);
                }

                _folded += FoldConc(root);   // before note conversion: completes TEXT values

                if (root.Tag == "INDI")
                {
                    _inlineConverted += ConvertInlineNameCitations(root);
                    _aliases += ConvertAliasNicknames(root);
                }
            }

            ConvertNoteRecordsToSnote();
            ConvertFreeTextCitations();
            _emptyContacts += doc.Records.Sum(StripEmptyContactLines);

            _submRemoved += doc.MutableRecords.RemoveAll(
                r => r.Tag == "SUBM" && bareSubmXrefs.Contains(r.Xref ?? ""));

            int schemaTagsDeclared = _head is null ? 0 : DeclareSchemaTags(_head);

            doc.RebuildXrefIndex();

            return new UpgradeSummary(
                _folded, _headerRemoved, _inlineConverted, _noteRecords,
                _freeTextCitations, _aliases, _emptyContacts, _submRemoved,
                schemaTagsDeclared);
        }

        // -------------------------------------------------------------------
        // HEAD.SCHMA — declare every extension tag the upgraded document uses
        // -------------------------------------------------------------------

        /// <summary>
        /// Ensure every extension tag (starting with '_') present anywhere in the
        /// document — counting a surviving CONC node as '_CONC', the tag
        /// <see cref="Ged70Formatter"/> writes it as — has a
        /// <c>HEAD.SCHMA.TAG</c> entry. CONC lines this run folded away need no
        /// declaration: the written file will not contain them. Existing
        /// declarations are never modified. Returns the number of new TAG lines
        /// added.
        /// </summary>
        int DeclareSchemaTags(GedRecord head)
        {
            var tags = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in doc.Records)
                CollectExtensionTags(root, tags);
            if (tags.Count == 0) return 0;

            var schma = head.FirstChild("SCHMA");
            if (schma is null)
            {
                schma = new GedRecord(1, null, "SCHMA", "") { Parent = head };
                int gedcIndex = head.Children.FindIndex(c => c.Tag == "GEDC");
                head.Children.Insert(gedcIndex >= 0 ? gedcIndex + 1 : head.Children.Count, schma);
            }

            var declared = schma.ChildrenByTag("TAG")
                .Select(t => FirstToken(t.FullValue()))
                .ToHashSet(StringComparer.Ordinal);

            int added = 0;
            foreach (var tag in tags)
            {
                if (declared.Contains(tag)) continue;
                schma.Children.Add(
                    new GedRecord(2, null, "TAG", $"{tag} {SchemaTagUri(tag)}") { Parent = schma });
                added++;
            }
            return added;
        }

        static void CollectExtensionTags(GedRecord rec, SortedSet<string> tags)
        {
            if (rec.Tag.Length > 0 && rec.Tag[0] == '_') tags.Add(rec.Tag);
            else if (rec.Tag == "CONC") tags.Add("_CONC");   // written as _CONC by Ged70Formatter
            foreach (var child in rec.Children)
                CollectExtensionTags(child, tags);
        }

        static string FirstToken(string value)
        {
            int space = value.IndexOf(' ');
            return space < 0 ? value : value[..space];
        }

        static string SchemaTagUri(string tag) => $"https://github.com/morrillonline/GedFire/gedcom/{tag}";

        void CleanHeader(GedRecord head, HashSet<string> bareSubmXrefs)
        {
            _headerRemoved += head.Children.RemoveAll(
                c => c.Tag is "CHAR" or "FILE" or "DEST"
                  || (c.Tag == "SUBM" && bareSubmXrefs.Contains(c.Value)));
            var gedc = head.FirstChild("GEDC");
            if (gedc is not null)
                _headerRemoved += gedc.Children.RemoveAll(c => c.Tag == "FORM");
        }

        // -------------------------------------------------------------------
        // NOTE records → SNOTE ("0 @X@ NOTE" is 5.5; 7.0 shared notes are SNOTE)
        // -------------------------------------------------------------------

        void ConvertNoteRecordsToSnote()
        {
            var converted = new HashSet<string>();
            foreach (var root in doc.Records)
            {
                if (root.Tag != "NOTE" || root.Xref is null) continue;
                root.Retag("SNOTE");
                converted.Add(root.Xref);
                _noteRecords++;
            }
            if (converted.Count == 0) return;

            foreach (var root in doc.Records)
                RetagNotePointers(root, converted);
        }

        static void RetagNotePointers(GedRecord rec, HashSet<string> targets)
        {
            foreach (var child in rec.Children)
            {
                if (child.Tag == "NOTE" && targets.Contains(child.Value))
                    child.Retag("SNOTE");
                RetagNotePointers(child, targets);
            }
        }

        // -------------------------------------------------------------------
        // Free-text source citations → pointer to a new SOUR record.
        // -------------------------------------------------------------------

        void ConvertFreeTextCitations()
        {
            var citations = new List<GedRecord>();
            foreach (var root in doc.Records)
                if (root.Tag != "HEAD")   // HEAD.SOUR is the producing-system id
                    CollectFreeTextCitations(root, citations);
            if (citations.Count == 0) return;

            var sourceByText = new Dictionary<string, GedRecord>();
            int nextNum = 1 + doc.Records
                .Where(r => r.Tag == "SOUR" && r.Xref is not null)
                .Select(r => XrefNumber(r.Xref!))
                .DefaultIfEmpty(0).Max();
            int width = doc.Records
                .Where(r => r.Tag == "SOUR" && r.Xref is not null)
                .Select(r => r.Xref!.Length - 3)   // digits between "@S" and "@"
                .DefaultIfEmpty(5).Max();

            foreach (var citation in citations)
            {
                string text = citation.FullValue();
                if (!sourceByText.TryGetValue(text, out var source))
                {
                    string xref = $"@S{nextNum++.ToString().PadLeft(width, '0')}@";
                    source = new GedRecord(0, xref, "SOUR", "");
                    AppendNoteWithText(source, text);
                    sourceByText[text] = source;
                }

                citation.SetValue(source.Xref!);
                // The text now lives on the SOUR record; drop the citation's
                // continuation lines (other substructures are preserved).
                citation.Children.RemoveAll(c => c.Tag is "CONT" or "CONC");
                _freeTextCitations++;
            }

            // Keep source records grouped: insert after the last existing SOUR.
            int insertAt = doc.MutableRecords.FindLastIndex(r => r.Tag == "SOUR") + 1;
            if (insertAt == 0) insertAt = doc.MutableRecords.Count - 1;   // before TRLR
            doc.MutableRecords.InsertRange(insertAt, sourceByText.Values);
        }

        static void CollectFreeTextCitations(GedRecord rec, List<GedRecord> found)
        {
            foreach (var child in rec.Children)
            {
                if (child.Tag == "SOUR" && child.Value.Length > 0 && !child.IsPointerValue)
                    found.Add(child);
                CollectFreeTextCitations(child, found);
            }
        }

        static void AppendNoteWithText(GedRecord parent, string text)
        {
            string[] lines = text.Split('\n');
            var note = new GedRecord(1, null, "NOTE", GedRecord.EscapeAtSign(lines[0])) { Parent = parent };
            for (int i = 1; i < lines.Length; i++)
                note.Children.Add(new GedRecord(2, null, "CONT", lines[i]) { Parent = note });
            parent.Children.Add(note);
        }

        static int XrefNumber(string xref)
        {
            // "@S00257@" → 257; xrefs without a numeric part rank as 0.
            int start = 2, end = xref.Length - 1;
            return start < end && int.TryParse(xref[start..end], out int n) ? n : 0;
        }

        // -------------------------------------------------------------------
        // ALIA with text payload → NICK name piece (7.0 ALIA must be a pointer)
        // -------------------------------------------------------------------

        int ConvertAliasNicknames(GedRecord indi)
        {
            var name = indi.FirstChild("NAME");
            if (name is null) return 0;

            int converted = 0;
            foreach (var alias in indi.ChildrenByTag("ALIA").ToList())
            {
                if (alias.Value.Length == 0 || alias.IsPointerValue) continue;
                name.Children.Add(
                    new GedRecord(2, null, "NICK", alias.FullValue()) { Parent = name });
                indi.Children.Remove(alias);
                converted++;
            }
            return converted;
        }

        // -------------------------------------------------------------------
        // Empty ADDR/EMAIL/PHON/FAX/WWW lines — invalid in 7.0 (payload required)
        // -------------------------------------------------------------------

        static int StripEmptyContactLines(GedRecord rec)
        {
            int removed = rec.Children.RemoveAll(
                c => c.Tag is "ADDR" or "EMAIL" or "PHON" or "FAX" or "WWW"
                  && c.Value.Length == 0 && c.Children.Count == 0);
            return removed + rec.Children.Sum(StripEmptyContactLines);
        }

        // -------------------------------------------------------------------
        // CONC folding
        // -------------------------------------------------------------------

        /// <summary>
        /// Remove every CONC descendant of <paramref name="rec"/>, appending its
        /// text to the line it continues.  Returns the number of lines folded.
        /// </summary>
        static int FoldConc(GedRecord rec)
        {
            int folded = 0;
            var sink = rec;                 // line that the next CONC continues
            var survivors = new List<GedRecord>(rec.Children.Count);

            foreach (var child in rec.Children)
            {
                if (child.Tag == "CONC")
                {
                    sink.AppendValue(child.Value);
                    folded++;
                    continue;
                }

                if (child.Tag == "CONT") sink = child;
                else folded += FoldConc(child);
                survivors.Add(child);
            }

            if (survivors.Count != rec.Children.Count)
            {
                rec.Children.Clear();
                rec.Children.AddRange(survivors);
            }
            return folded;
        }

        // -------------------------------------------------------------------
        // "Inline: TRUE|" narrative citations → NOTE-with-citation
        // -------------------------------------------------------------------

        /// <summary>
        /// Convert each "Inline: TRUE|…" narrative citation under the individual's
        /// NAME into a person-level NOTE-with-citation, appended to the INDI in
        /// original citation order (which preserves the generator's prose-paragraph
        /// order and footnote numbering).  Returns the number converted.
        /// </summary>
        static int ConvertInlineNameCitations(GedRecord indi)
        {
            // Collected first, appended after the scan: adding to indi.Children
            // while ChildrenByTag is enumerating it would invalidate the iterator.
            List<GedRecord>? narrativeNotes = null;

            foreach (var name in indi.ChildrenByTag("NAME"))
            {
                List<GedRecord>? replaced = null;
                foreach (var sour in name.ChildrenByTag("SOUR"))
                {
                    var note = TryBuildNarrativeNote(sour);
                    if (note is null) continue;

                    note.Parent = indi;
                    (narrativeNotes ??= []).Add(note);
                    (replaced ??= []).Add(sour);
                }
                if (replaced is not null)
                    name.Children.RemoveAll(replaced.Contains);
            }

            if (narrativeNotes is null) return 0;
            indi.Children.AddRange(narrativeNotes);
            return narrativeNotes.Count;
        }

        /// <summary>
        /// If <paramref name="sour"/> is a pure narrative citation
        /// (DATA/TEXT payload marked "Inline: TRUE|"), build its replacement:
        /// a level-1 NOTE carrying the prose, with a level-2 SOUR citation that
        /// inherits every non-DATA substructure of the original (PAGE, _FOOT, …
        /// — all already at the correct level).  Returns null when the citation
        /// should be left as-is.
        /// </summary>
        static GedRecord? TryBuildNarrativeNote(GedRecord sour)
        {
            var data = sour.FirstChild("DATA");
            var text = data?.FirstChild("TEXT");
            if (data is null || text is null || data.Children.Count != 1) return null;

            string prose = FtmCitationText.ParsePropertyList(text.FullValue(),
                out bool isInlineable, out bool noCitation, out string shortCitation);
            if (!isInlineable || noCitation || shortCitation.Length > 0 || prose.Length == 0)
                return null;

            string[] lines = prose.Split('\n');
            var note = new GedRecord(1, null, "NOTE", lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                var cont = new GedRecord(2, null, "CONT", lines[i]) { Parent = note };
                note.Children.Add(cont);
            }

            var citation = new GedRecord(2, null, "SOUR", sour.Value) { Parent = note };
            foreach (var child in sour.Children)
            {
                if (ReferenceEquals(child, data)) continue;
                child.Parent = citation;
                citation.Children.Add(child);
            }
            note.Children.Add(citation);

            return note;
        }
    }
}

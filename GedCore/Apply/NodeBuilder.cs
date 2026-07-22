using System.Text.RegularExpressions;

namespace GedCore.Apply;

/// <summary>
/// Node-construction and structure-editing helpers shared by the op classes.
/// File conventions live here: zero-padded days, event detail (DATE/PLAC)
/// ahead of citations/links/notes, vitals inserted before FAMS/FAMC/NOTE/UID,
/// citation sub-order PAGE → DATA → QUAY.
/// </summary>
internal static class NodeBuilder
{
    private static readonly Regex UnpaddedDay = new(@"^(\d) ([A-Z]{3} \d{4})$", RegexOptions.Compiled);

    public static GedRecord NewNode(int level, string tag, string value) =>
        new(level, null, tag, value);

    public static GedRecord NoteNode(int level, string text, string? mime = null)
    {
        var note = NewNode(level, "NOTE", "");
        SetNoteText(note, text);
        SetNoteMime(note, mime);
        return note;
    }

    /// <summary>Write multi-line note text as a NOTE payload followed by CONT children.</summary>
    public static void SetNoteText(GedRecord note, string text)
    {
        string[] lines = NormalizeText(text).Split('\n');
        note.SetValue(GedRecord.EscapeAtSign(lines[0]));
        note.Children.RemoveAll(child => child.Tag is "CONT" or "CONC" or "_CONC");
        for (int index = 1; index < lines.Length; index++)
            Attach(note, NewNode(note.Level + 1, "CONT", lines[index]), at: index - 1);
    }

    public static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>
    /// Set the GEDCOM 7 MIME type for a NOTE. Plain text is represented by no
    /// MIME child, preserving the conventional compact form.
    /// </summary>
    public static bool SetNoteMime(GedRecord note, string? mime)
    {
        var existing = note.FirstChild("MIME");
        if (mime is null)
        {
            if (existing is null) return false;
            note.Children.Remove(existing);
            return true;
        }

        if (existing is not null)
        {
            if (existing.Value == mime) return false;
            existing.SetValue(mime);
            return true;
        }

        int at = note.Children.FindLastIndex(child => child.Tag is "CONT" or "CONC" or "_CONC") + 1;
        Attach(note, NewNode(note.Level + 1, "MIME", mime), at);
        return true;
    }

    public static void Attach(GedRecord parent, GedRecord child, int? at = null)
    {
        child.Parent = parent;
        if (at is int i) parent.Children.Insert(i, child);
        else parent.Children.Add(child);
    }

    /// <summary>'2 JUN 1949' → '02 JUN 1949', matching the file's zero-padded style.</summary>
    public static string PadDay(string date)
    {
        var m = UnpaddedDay.Match(date);
        return m.Success ? $"0{m.Groups[1].Value} {m.Groups[2].Value}" : date;
    }

    public static GedRecord EventNode(int level, string tag, EventValue? value,
                                      IReadOnlyList<Citation> citations)
    {
        var node = NewNode(level, tag, GedRecord.EscapeAtSign(value?.Text ?? ""));
        if (value?.Date is not null) Attach(node, NewNode(level + 1, "DATE", PadDay(value.Date)));
        if (value?.Place is not null) Attach(node, NewNode(level + 1, "PLAC", value.Place));
        foreach (var cit in citations)
            Attach(node, CitationNode(level + 1, cit));
        return node;
    }

    public static GedRecord CitationNode(int level, Citation cit)
    {
        var sour = NewNode(level, "SOUR", cit.Source);
        if (cit.Page is not null) Attach(sour, NewNode(level + 1, "PAGE", cit.Page));
        if (cit.DataText is not null)
        {
            var data = NewNode(level + 1, "DATA", "");
            Attach(data, NewNode(level + 2, "TEXT", GedRecord.EscapeAtSign(cit.DataText)));
            Attach(sour, data);
        }
        if (cit.Quay is not null) Attach(sour, NewNode(level + 1, "QUAY", cit.Quay.Value.ToString()));
        return sour;
    }

    /// <summary>
    /// Insert after the last existing record of <paramref name="tag"/>, keeping
    /// same-type records grouped. When the file carries none yet (e.g. the
    /// first-ever <c>OBJE</c>), falls back to just before <c>TRLR</c> — the same
    /// fallback <c>Ged70Upgrader</c> uses when it introduces the file's first
    /// free-text-derived <c>SOUR</c> record.
    /// </summary>
    public static void InsertAfterLastOfKind(GedDocument doc, string tag, GedRecord rec)
    {
        int last = -1;
        for (int i = 0; i < doc.Records.Count; i++)
            if (doc.Records[i].Tag == tag) last = i;
        int insertAt = last < 0 ? doc.MutableRecords.Count - 1 : last + 1;
        doc.MutableRecords.Insert(insertAt, rec);
    }

    /// <summary>
    /// Insert an event-detail node (DATE/PLAC) ahead of any citation/link/note,
    /// so event detail stays before SOUR/OBJE/NOTE as the file convention expects.
    /// </summary>
    public static void InsertEventDetail(GedRecord fact, GedRecord node)
    {
        int anchor = fact.Children.FindIndex(c => c.Tag is "SOUR" or "OBJE" or "NOTE" or "SNOTE");
        Attach(fact, node, at: anchor < 0 ? null : anchor);
    }

    /// <summary>
    /// Insert a new vital fact on a record, ahead of links/notes/UID so events
    /// stay grouped, matching the file's layout.
    /// </summary>
    public static void InsertFact(GedRecord record, GedRecord fact)
    {
        int anchor = record.Children.FindIndex(
            c => c.Tag is "FAMS" or "FAMC" or "NOTE" or "SNOTE" or "UID");
        Attach(record, fact, at: anchor < 0 ? null : anchor);
    }

    /// <summary>
    /// Rebuild a subtree at a new level (GedRecord.Level is fixed at
    /// construction), used to move a citation from a fact to a NOTE.
    /// </summary>
    public static GedRecord Relevel(GedRecord node, int level)
    {
        var copy = NewNode(level, node.Tag, node.Value);
        foreach (var child in node.Children)
            Attach(copy, Relevel(child, level + 1));
        return copy;
    }

    /// <summary>
    /// CreateOrUpdate one citation on a structure (a fact node, or a FAM
    /// record for relationship provenance). Source not present → create.
    /// Present with every requested field equal → no-op (returns null).
    /// Present with a differing field → update in place, returning a log
    /// fragment naming what changed. Fields the request omits are left alone.
    /// The "one citation per source per structure" invariant holds by
    /// construction.
    /// </summary>
    public static string? UpsertCitation(ApplyState state, GedRecord structure, Citation cit)
    {
        var existing = Resolve.CitationOnStructure(structure, cit.Source);
        if (existing is null)
        {
            // record-level provenance (FAM) sits before trailing NOTE/UID;
            // fact-level citations go last within the fact
            int? at = null;
            if (structure.Xref is not null)
            {
                int anchor = structure.Children.FindIndex(c => c.Tag is "NOTE" or "SNOTE" or "UID");
                if (anchor >= 0) at = anchor;
            }
            var node = CitationNode(structure.Level + 1, cit);
            RecordCitationFields(state, node, cit);
            Attach(structure, node, at);
            state.Mutated();
            state.Touch(structure);
            return $"cited {cit.Source}";
        }

        RecordCitationFields(state, existing, cit);
        var changes = new List<string>();
        if (cit.Page is not null)
            UpsertSub(existing, "PAGE", cit.Page, at: 0, changes);
        if (cit.DataText is not null)
        {
            var data = existing.FirstChild("DATA");
            if (data is null)
            {
                data = NewNode(existing.Level + 1, "DATA", "");
                int at = existing.FirstChild("PAGE") is null ? 0 : 1;
                Attach(existing, data, at);
            }
            UpsertSub(data, "TEXT", GedRecord.EscapeAtSign(cit.DataText), at: 0, changes);
        }
        if (cit.Quay is not null)
            UpsertSub(existing, "QUAY", cit.Quay.Value.ToString(), at: null, changes);

        if (changes.Count == 0) return null;
        state.Mutated();
        state.Touch(structure);
        return $"updated citation {cit.Source} ({string.Join("; ", changes)})";
    }

    /// <summary>
    /// Register this citation's field writes with the run so a second op that
    /// would overwrite an earlier op's differing value on the same citation
    /// node fails the run cleanly instead of silently discarding it.
    /// </summary>
    private static void RecordCitationFields(ApplyState state, GedRecord citation, Citation cit)
    {
        if (cit.Page is not null) state.RecordCitationField(citation, cit.Source, "PAGE", cit.Page);
        if (cit.DataText is not null) state.RecordCitationField(citation, cit.Source, "DATA.TEXT", cit.DataText);
        if (cit.Quay is not null)
            state.RecordCitationField(citation, cit.Source, "QUAY", cit.Quay.Value.ToString());
    }

    private static void UpsertSub(GedRecord parent, string tag, string value,
                                  int? at, List<string> changes)
    {
        var sub = parent.FirstChild(tag);
        if (sub is null)
        {
            Attach(parent, NewNode(parent.Level + 1, tag, value), at);
            changes.Add($"{tag} added '{value}'");
        }
        else if (sub.Value != value)
        {
            changes.Add($"{tag} '{sub.Value}' → '{value}'");
            sub.SetValue(value);
        }
    }

    /// <summary>
    /// Disposition of citation(s) attached to a fact whose value is being
    /// replaced or deleted:
    ///   "keep"       — leave them in place (only meaningful for updates);
    ///   "drop"       — remove them;
    ///   "moveToNote" — re-attach them beneath a NOTE on the record (GEDCOM 7
    ///                  allows SOUR under NOTE) so the prior attestation stays
    ///                  auditable against the old value. Reuses the NOTE a
    ///                  note op added earlier in the same item if present;
    ///                  otherwise creates a minimal superseded-value note.
    /// </summary>
    public static void DisposeCitations(ApplyState state, GedRecord record,
                                        GedRecord factNode, string factTag, string mode)
    {
        if (mode == "keep") return;

        var citations = factNode.ChildrenByTag("SOUR").ToList();
        if (citations.Count == 0) return;

        foreach (var cit in citations)
            factNode.Children.Remove(cit);
        state.Mutated();
        state.Touch(record);

        if (mode == "drop") return;

        if (!state.NotesAddedThisItem.TryGetValue(record.Xref!, out var note))
        {
            note = NewNode(1, "NOTE",
                $"Prior citation(s) on {factTag} superseded by a correction and retained here.");
            Attach(record, note);
            state.NotesAddedThisItem[record.Xref!] = note;
        }
        foreach (var cit in citations)
            Attach(note, Relevel(cit, note.Level + 1));
    }
}

/// <summary>Validation helpers shared by the op classes.</summary>
internal static class OpChecks
{
    /// <summary>
    /// Reject the reserved <see cref="GedRecord.VoidPointer"/> wherever an op
    /// field names a record it expects to target (as opposed to create).
    /// Returns true (having appended the error) when <paramref name="xref"/>
    /// is <c>@VOID@</c>, so the caller can short-circuit its own not-found
    /// check with one consistent message.
    /// </summary>
    public static bool RejectVoid(string context, string xref, List<string> errors)
    {
        if (xref != GedRecord.VoidPointer) return false;
        errors.Add($"{context}: {GedRecord.VoidPointer} is not an addressable record");
        return true;
    }

    public static void CitationsRequired(string context, IReadOnlyList<Citation> citations,
                                         List<string> errors)
    {
        if (citations.Count == 0)
            errors.Add($"{context}: citation required (uncited facts are not accepted)");
    }

    public static void CitationsValid(ResolutionContext ctx, string context,
                                      IReadOnlyList<Citation> citations, List<string> errors)
    {
        foreach (var cit in citations)
        {
            if (cit.Source == GedRecord.VoidPointer)
                errors.Add($"{context}: citation source {GedRecord.VoidPointer} is not an addressable record");
            else if (!ctx.Known(cit.Source))
                errors.Add($"{context}: citation source {cit.Source} unknown");
            // A citation value carrying a raw line break serializes to an
            // unparseable line (the tool does not split PAGE/TEXT into CONT
            // continuation lines); reject it here so it fails at validation
            // with a clear message rather than in the reparse-verify step.
            if (HasLineBreak(cit.Page))
                errors.Add($"{context}: citation {cit.Source} page contains a line break — " +
                           "citation values must be single-line");
            if (HasLineBreak(cit.DataText))
                errors.Add($"{context}: citation {cit.Source} dataText contains a line break — " +
                           "flatten it to a single line (join phrases with '. ')");
        }
        foreach (var dup in citations.GroupBy(c => c.Source).Where(g => g.Count() > 1))
            errors.Add($"{context}: source {dup.Key} cited twice on one structure");
    }

    private static bool HasLineBreak(string? value) =>
        value is not null && (value.Contains('\n') || value.Contains('\r'));


    /// <summary>Validates citations supplied on facts carried inline by a new-person description.</summary>
    public static void InlineFactsValid(ResolutionContext ctx, string context,
                                        PersonRef person, List<string> errors)
    {
        foreach (var fact in person.Facts)
        {
            CitationsValid(ctx, $"{context}: inline {fact.Fact} on {person.Xref}", fact.Citations, errors);
        }
    }
}

using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// CreateOrUpdate for the Note noun: research reasoning and conflict analysis
/// (never a dated fact assertion). Key: (record, exact full text). A note with the
/// exact requested text → no-op; "match" naming an existing note's full text →
/// that note is rewritten; otherwise → create. The touched note registers as
/// this item's note so a moveToNote disposition in the same item reuses it —
/// including on re-runs, which keeps them no-ops.
///
/// Optional <c>citations</c> attach SOUR structures beneath the NOTE (GEDCOM 7
/// allows a citation under a NOTE). This is the narrative-biography form: the
/// generator renders a cited INDI-level note as a prose paragraph with the
/// source footnoted, rather than as plain note text. Citations are upserted
/// idempotently, so re-applying leaves the file byte-identical.
///
/// Purpose "biography" says the note is the person's cited biographical prose
/// and records the citations the op added for biography credit
/// (<see cref="BiographyNotes"/>); the purpose lives in the changeset only and
/// writes nothing extra to the file. A note a biography op wrote never
/// receives moveToNote citations in the same run.
/// </summary>
public sealed class CreateOrUpdateNoteOp : ChangeOp
{
    public const string BiographyPurpose = "biography";

    public override string Kind => "createOrUpdateNote";

    public required string Record { get; init; }
    public required string Text { get; init; }
    public string? Match { get; init; }
    public string? Mime { get; init; }
    public string? Purpose { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    bool IsBiography => string.Equals(Purpose?.Trim(), BiographyPurpose, StringComparison.OrdinalIgnoreCase);

    internal override IEnumerable<string> CitedSources => Citations.Select(c => c.Source);

    internal static CreateOrUpdateNoteOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "createOrUpdateNote"),
        Text = JsonRead.Req(el, "text", "createOrUpdateNote"),
        Match = JsonRead.Str(el, "match"),
        Mime = JsonRead.Str(el, "mime"),
        Purpose = JsonRead.Str(el, "purpose"),
        Citations = Citation.ReadAll(el),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        ProseXrefGuard.Check($"{Kind} on {Record}", "text", Text, errors);
        if (OpChecks.RejectVoid($"{Kind} on {Record}", Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Kind} on {Record}: target not in file"); return; }
        if (Mime is not null && !IsSupportedMime(Mime))
            errors.Add($"{Kind} on {Record}: mime must be text/plain or text/html");
        if (Purpose is not null && !IsBiography)
            errors.Add($"{Kind} on {Record}: purpose must be \"{BiographyPurpose}\"");
        else if (IsBiography && !IsPerson(ctx))
            errors.Add($"{Kind} on {Record}: a biography note belongs on a person");
        // Citations are optional on a note, but any given must name a known source.
        OpChecks.CitationsValid(ctx, $"{Kind} on {Record}", Citations, errors);
        var target = ctx.Existing(Record);
        if (target is null || Match is null) return;
        string matchText = NodeBuilder.NormalizeText(Match);
        string requestedText = NodeBuilder.NormalizeText(Text);
        bool found = target.ChildrenByTag("NOTE").Any(note =>
            note.FullValue() == matchText || note.FullValue() == requestedText);
        if (!found)
            errors.Add($"{Kind} on {Record}: no note matches \"match\" (and none already has the new text)");
    }

    bool IsPerson(ResolutionContext ctx) =>
        ctx.Existing(Record) is { } existing
            ? existing.Tag == "INDI"
            : ctx.Placeholders.TryGetKind(Record, out var kind) && kind == PlaceholderKind.Person;

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[state.Resolve(Record)];
        var (note, action) = WriteText(state, target);

        if (Mime is not null && NodeBuilder.SetNoteMime(note, NormalizeMime(Mime)))
        {
            Changed(state, target);
            action ??= "updated (MIME changed)";
        }

        var heldCitations = BiographyNotes.CitationsOf(note).ToHashSet();
        state.NotesAddedThisItem[target.Xref!] = note;

        var citeChanges = CitationReconciler.Upsert(state, note, state.ResolveCitations(Citations));
        if (IsBiography)
        {
            state.BiographyNotesWritten.Add(note);
            state.AddBiographyCitations(target.Xref!, BiographyNotes.CitationsOf(note).Where(c => !heldCitations.Contains(c)));
        }

        var parts = new[] { action }.OfType<string>().Concat(citeChanges).ToList();
        log.Add($"{Kind} on {target.Xref}: {(parts.Count == 0 ? "no-op (already present)" : string.Join("; ", parts))}");
    }

    // The note with the requested text, else the matched note rewritten, else a new note; the action is null when the text was already present.
    (GedRecord Note, string? Action) WriteText(ApplyState state, GedRecord target)
    {
        string requestedText = NodeBuilder.NormalizeText(Text);
        var identical = target.ChildrenByTag("NOTE").FirstOrDefault(note => note.FullValue() == requestedText);
        if (identical is not null) return (identical, null);

        var matched = Match is null
            ? null
            : target.ChildrenByTag("NOTE").FirstOrDefault(note => note.FullValue() == NodeBuilder.NormalizeText(Match));
        if (matched is not null)
        {
            NodeBuilder.SetNoteText(matched, requestedText);
            Changed(state, target);
            return (matched, "updated (text rewritten)");
        }

        var created = NodeBuilder.NoteNode(1, requestedText, Mime is null ? null : NormalizeMime(Mime));
        NodeBuilder.Attach(target, created);
        Changed(state, target);
        return (created, "created");
    }

    static void Changed(ApplyState state, GedRecord target)
    {
        state.Mutated();
        state.Touch(target);
    }

    static string? NormalizeMime(string mime) => mime.Trim().ToLowerInvariant() switch
    {
        "text/plain" => null,
        "text/html" => "text/html",
        _ => null,
    };

    static bool IsSupportedMime(string mime) =>
        mime.Trim().Equals("text/plain", StringComparison.OrdinalIgnoreCase)
        || mime.Trim().Equals("text/html", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Delete for the Note noun, keyed by the note's exact full text. Absent → no-op.</summary>
public sealed class DeleteNoteOp : ChangeOp
{
    public override string Kind => "deleteNote";

    public required string Record { get; init; }
    public required string Text { get; init; }

    internal static DeleteNoteOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "deleteNote"),
        Text = JsonRead.Req(el, "text", "deleteNote"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (!ctx.Known(Record))
            errors.Add($"{Kind} on {Record}: target not in file");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        // Validate accepts a Record that ctx.Known() resolves through the
        // placeholder plan (a record an earlier op in this changeset
        // created), so Apply must resolve it before using it as a doc key.
        var record = state.Resolve(Record);
        var target = state.Doc.ByXref[record];
        string requestedText = NodeBuilder.NormalizeText(Text);
        var note = target.ChildrenByTag("NOTE").FirstOrDefault(note => note.FullValue() == requestedText);
        if (note is null)
        {
            log.Add($"{Kind} on {record}: no-op (absent)");
            return;
        }
        target.Children.Remove(note);
        state.Mutated();
        state.Touch(target);
        log.Add($"{Kind} on {record}: deleted");
    }
}

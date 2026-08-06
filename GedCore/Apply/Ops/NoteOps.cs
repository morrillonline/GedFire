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
/// </summary>
public sealed class CreateOrUpdateNoteOp : ChangeOp
{
    public override string Kind => "createOrUpdateNote";

    public required string Record { get; init; }
    public required string Text { get; init; }
    public string? Match { get; init; }
    public string? Mime { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    internal override IEnumerable<string> CitedSources => Citations.Select(c => c.Source);

    internal static CreateOrUpdateNoteOp Read(JsonElement el) => new()
    {
        Record = JsonRead.Req(el, "record", "createOrUpdateNote"),
        Text = JsonRead.Req(el, "text", "createOrUpdateNote"),
        Match = JsonRead.Str(el, "match"),
        Mime = JsonRead.Str(el, "mime"),
        Citations = Citation.ReadAll(el),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid($"{Kind} on {Record}", Record, errors)) return;
        if (!ctx.Known(Record)) { errors.Add($"{Kind} on {Record}: target not in file"); return; }
        if (Mime is not null && !IsSupportedMime(Mime))
            errors.Add($"{Kind} on {Record}: mime must be text/plain or text/html");
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

    internal override void Apply(ApplyState state, List<string> log)
    {
        var target = state.Doc.ByXref[Record];
        string requestedText = NodeBuilder.NormalizeText(Text);
        string? requestedMime = Mime is null ? null : NormalizeMime(Mime);

        // Get-or-create the note node, then upsert citations onto it. Keeping the
        // text and the citations in one idempotent pass means a re-run is a no-op
        // whether or not the note already carries the sources.
        GedRecord note;
        string action;
        var identical = target.ChildrenByTag("NOTE").FirstOrDefault(note => note.FullValue() == requestedText);
        if (identical is not null)
        {
            note = identical;
            action = "no-op (already present)";
        }
        else
        {
            var matched = Match is null
                ? null
                : target.ChildrenByTag("NOTE").FirstOrDefault(note =>
                    note.FullValue() == NodeBuilder.NormalizeText(Match));
            if (matched is not null)
            {
                NodeBuilder.SetNoteText(matched, requestedText);
                state.Mutated();
                state.Touch(target);
                note = matched;
                action = "updated (text rewritten)";
            }
            else
            {
                note = NodeBuilder.NoteNode(1, requestedText, requestedMime);
                NodeBuilder.Attach(target, note);
                state.Mutated();
                state.Touch(target);
                action = "created";
            }
        }

        if (Mime is not null && NodeBuilder.SetNoteMime(note, requestedMime))
        {
            state.Mutated();
            state.Touch(target);
            action = action == "no-op (already present)" ? "updated (MIME changed)" : action;
        }
        state.NotesAddedThisItem[Record] = note;

        var citeChanges = new List<string>();
        foreach (var cit in Citations)
        {
            var change = NodeBuilder.UpsertCitation(state, note, cit);
            if (change is not null) citeChanges.Add(change);
        }

        if (citeChanges.Count > 0)
            action = action == "no-op (already present)"
                ? string.Join("; ", citeChanges)
                : $"{action}; {string.Join("; ", citeChanges)}";
        log.Add($"{Kind} on {Record}: {action}");
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
        var target = state.Doc.ByXref[Record];
        string requestedText = NodeBuilder.NormalizeText(Text);
        var note = target.ChildrenByTag("NOTE").FirstOrDefault(note => note.FullValue() == requestedText);
        if (note is null)
        {
            log.Add($"{Kind} on {Record}: no-op (absent)");
            return;
        }
        target.Children.Remove(note);
        state.Mutated();
        state.Touch(target);
        log.Add($"{Kind} on {Record}: deleted");
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace GedCore.Apply;

/// <summary>
/// CreateOrUpdate for the Media noun: an <c>OBJE</c> record (one or more
/// files plus an optional record title) and, optionally, links attaching it
/// to people/families. Key: <see cref="Xref"/> when given; otherwise the
/// exact set of <see cref="Files"/> paths (so re-submitting the same photo
/// without an xref is still idempotent). Absent → create; present with equal
/// title and files → no-op; a differing title is replaced and a differing
/// files list replaces the FILE substructures wholesale — both logged.
/// Each <see cref="AttachTo"/> entry upserts a <c>1 OBJE</c> link on the
/// named person or family — already linked → no-op (title/portrait changes
/// still apply); not yet linked → attach. <c>portrait: true</c> moves the
/// link before any other <c>OBJE</c> link already on that target, since the
/// generator treats an individual's first media link as their portrait
/// ("preferred first", Subproject H).
/// </summary>
public sealed class CreateOrUpdateMediaOp : ChangeOp
{
    private static readonly Regex MediaTypePattern = new(@"^[-\w.+]+/[-\w.+]+$", RegexOptions.Compiled);

    public override string Kind => "createOrUpdateMedia";

    public string? Xref { get; init; }
    public string? Title { get; init; }
    public required IReadOnlyList<MediaFileRequest> Files { get; init; }
    public IReadOnlyList<MediaAttachRequest> AttachTo { get; init; } = [];

    internal static CreateOrUpdateMediaOp Read(JsonElement el) => new()
    {
        Xref = JsonRead.Str(el, "xref"),
        Title = JsonRead.Str(el, "title"),
        Files = MediaFileRequest.ReadList(el, "files"),
        AttachTo = MediaAttachRequest.ReadList(el, "attachTo"),
    };

    private string Context => Xref is not null ? $"{Kind} {Xref}" : Kind;

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (Xref is not null && OpChecks.RejectVoid(Kind, Xref, errors)) return;

        if (Files.Count == 0)
            errors.Add($"{Context}: files required (a media object needs at least one file)");
        foreach (var file in Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
                errors.Add($"{Context}: file path required");
            else if (!MediaTypePattern.IsMatch(file.MediaType))
                errors.Add($"{Context}: mediaType '{file.MediaType}' must be 'type/subtype'");
        }
        if (Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != Files.Count)
            errors.Add($"{Context}: duplicate file path in one media object");

        var existing = Xref is not null ? ctx.Existing(Xref) : MediaResolve.FindByFiles(ctx, Files);
        if (existing is not null && existing.Tag != "OBJE")
            errors.Add($"{Context}: not an OBJE record");
        else if (existing is null)
            ctx.Planned.Add(Xref ?? MediaResolve.NextFreeXref(ctx));

        foreach (var attach in AttachTo)
            ValidateAttach(ctx, attach, errors);
    }

    private void ValidateAttach(ResolutionContext ctx, MediaAttachRequest attach, List<string> errors)
    {
        if (attach.Person is null == (attach.Family is null))
        {
            errors.Add($"{Context}: attachTo entry needs exactly one of person/family");
            return;
        }
        string target = attach.Xref;
        if (OpChecks.RejectVoid(Context, target, errors)) return;
        if (!ctx.Known(target)) { errors.Add($"{Context}: attachTo target {target} not in file"); return; }
        var record = ctx.Existing(target);
        if (record is null) return;   // planned by an earlier op this run; exists by apply time
        string expectedTag = attach.IsPerson ? "INDI" : "FAM";
        if (record.Tag != expectedTag)
            errors.Add($"{Context}: attachTo target {target} is not a{(attach.IsPerson ? "n INDI" : " FAM")} record");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var ctx = new ResolutionContext(state.Doc);
        var existing = Xref is not null
            ? state.Doc.ByXref.GetValueOrDefault(Xref)
            : MediaResolve.FindByFiles(ctx, Files);

        GedRecord media;
        if (existing is null)
        {
            string xref = Xref ?? MediaResolve.NextFreeXref(ctx);
            media = new GedRecord(0, xref, "OBJE", "");
            if (Title is not null) NodeBuilder.Attach(media, NodeBuilder.NewNode(1, "TITL", Title));
            foreach (var file in Files) NodeBuilder.Attach(media, FileNode(file));
            state.AddRecord("OBJE", media);
            log.Add($"{Kind} {xref}: created ({Files.Count} file(s))");
        }
        else
        {
            media = existing;
            var changes = new List<string>();
            if (Title is not null) UpsertTitle(media, Title, changes);
            if (!FilesMatch(media)) ReplaceFiles(media, changes);
            if (changes.Count > 0) { state.Mutated(); state.Touch(media); }
            log.Add(changes.Count > 0
                ? $"{Kind} {media.Xref}: updated ({string.Join("; ", changes)})"
                : $"{Kind} {media.Xref}: no-op (already matches)");
        }

        foreach (var attach in AttachTo)
            AttachLink(state, state.Doc.ByXref[attach.Xref], media, attach, log);
    }

    private static GedRecord FileNode(MediaFileRequest file)
    {
        var node = NodeBuilder.NewNode(1, "FILE", MediaPaths.EscapeFilePath(file.Path));
        var form = NodeBuilder.NewNode(2, "FORM", file.MediaType);
        if (file.Medium is not null) NodeBuilder.Attach(form, NodeBuilder.NewNode(3, "MEDI", file.Medium));
        NodeBuilder.Attach(node, form);
        if (file.Title is not null) NodeBuilder.Attach(node, NodeBuilder.NewNode(2, "TITL", file.Title));
        return node;
    }

    /// <summary>The existing FILE substructures equal the requested list — ordered, all four fields.</summary>
    private bool FilesMatch(GedRecord media) =>
        media.ChildrenByTag("FILE")
            .Select(f => (Path: MediaPaths.UnescapeFilePath(f.FullValue()),
                          MediaType: f.FirstChild("FORM")?.Value ?? "",
                          Medium: f.FirstChild("FORM")?.FirstChild("MEDI")?.Value,
                          Title: f.FirstChild("TITL")?.FullValue()))
            .SequenceEqual(Files.Select(f => (f.Path, f.MediaType, f.Medium, f.Title)));

    /// <summary>
    /// createOrUpdate contract: a differing <c>files</c> list replaces the FILE
    /// substructures wholesale (no per-file merging), at the position the old
    /// ones occupied so surrounding structure order is preserved.
    /// </summary>
    private void ReplaceFiles(GedRecord media, List<string> changes)
    {
        var old = media.ChildrenByTag("FILE").ToList();
        int at = old.Count > 0 ? media.Children.IndexOf(old[0]) : media.Children.Count;
        foreach (var node in old) media.Children.Remove(node);
        int i = 0;
        foreach (var file in Files) NodeBuilder.Attach(media, FileNode(file), at + i++);
        changes.Add($"FILE list replaced ({old.Count} → {Files.Count} file(s))");
    }

    private static void UpsertTitle(GedRecord media, string title, List<string> changes)
    {
        var titl = media.FirstChild("TITL");
        if (titl is null)
        {
            NodeBuilder.Attach(media, NodeBuilder.NewNode(1, "TITL", title), at: 0);
            changes.Add($"TITL added '{title}'");
        }
        else if (titl.Value != title)
        {
            changes.Add($"TITL '{titl.Value}' \u2192 '{title}'");
            titl.SetValue(title);
        }
    }

    /// <summary>
    /// Upsert a <c>1 OBJE</c> link on <paramref name="target"/>. Already linked
    /// → title/portrait updates only; not yet linked → attach, ahead of any
    /// trailing NOTE/UID (the same anchor citations use), or before the
    /// target's first existing <c>OBJE</c> link when this one is the portrait.
    /// </summary>
    private static void AttachLink(ApplyState state, GedRecord target, GedRecord media,
                                   MediaAttachRequest attach, List<string> log)
    {
        var existingLink = target.ChildrenByTag("OBJE").FirstOrDefault(l => l.Value == media.Xref);
        if (existingLink is not null)
        {
            var changes = new List<string>();
            if (attach.Title is not null) UpsertLinkTitle(existingLink, attach.Title, changes);
            if (attach.Portrait && MakePortrait(target, existingLink)) changes.Add("moved first (portrait)");
            if (changes.Count > 0) { state.Mutated(); state.Touch(target); }
            log.Add(changes.Count > 0
                ? $"attach {media.Xref} on {target.Xref}: {string.Join("; ", changes)}"
                : $"attach {media.Xref} on {target.Xref}: no-op (already linked)");
            return;
        }

        var link = NodeBuilder.NewNode(1, "OBJE", media.Xref!);
        if (attach.Title is not null) NodeBuilder.Attach(link, NodeBuilder.NewNode(2, "TITL", attach.Title));

        int firstObje = target.Children.FindIndex(c => c.Tag == "OBJE");
        int trailing = target.Children.FindIndex(c => c.Tag is "NOTE" or "SNOTE" or "UID");
        int? at = attach.Portrait && firstObje >= 0 ? firstObje : (trailing < 0 ? null : trailing);
        NodeBuilder.Attach(target, link, at);
        state.Mutated();
        state.Touch(target);
        log.Add($"attach {media.Xref} on {target.Xref}: attached" + (attach.Portrait ? " (portrait)" : ""));
    }

    private static void UpsertLinkTitle(GedRecord link, string title, List<string> changes)
    {
        var titl = link.FirstChild("TITL");
        if (titl is null)
        {
            NodeBuilder.Attach(link, NodeBuilder.NewNode(link.Level + 1, "TITL", title));
            changes.Add($"TITL added '{title}'");
        }
        else if (titl.Value != title)
        {
            changes.Add($"TITL '{titl.Value}' \u2192 '{title}'");
            titl.SetValue(title);
        }
    }

    /// <summary>Move an existing link before the target's first OBJE link. Returns false if already first.</summary>
    private static bool MakePortrait(GedRecord target, GedRecord link)
    {
        int firstObje = target.Children.FindIndex(c => c.Tag == "OBJE");
        if (ReferenceEquals(target.Children[firstObje], link)) return false;
        target.Children.Remove(link);
        target.Children.Insert(firstObje, link);
        return true;
    }
}

/// <summary>
/// Delete for the Media noun. Removes the <c>OBJE</c> record and sweeps the
/// whole document for every <c>OBJE</c> link pointing at it (mirroring how
/// <c>Ged70Upgrader.RetagNotePointers</c> walks all records) — unlike the
/// other delete ops, a missing xref fails validation rather than being a
/// no-op, since "delete a photo that isn't there" is a composer mistake worth
/// surfacing, not a state to converge to silently.
/// </summary>
public sealed class DeleteMediaOp : ChangeOp
{
    public override string Kind => "deleteMedia";

    public required string Xref { get; init; }

    internal static DeleteMediaOp Read(JsonElement el) => new()
    {
        Xref = JsonRead.Req(el, "xref", "deleteMedia"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Kind, Xref, errors)) return;
        var existing = ctx.Existing(Xref);
        if (existing is null) { errors.Add($"{Kind} {Xref}: not in file"); return; }
        if (existing.Tag != "OBJE") errors.Add($"{Kind} {Xref}: not an OBJE record");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var existing = state.Doc.ByXref[Xref];
        state.RemoveRecord(existing);
        int linksRemoved = RemoveLinks(state, Xref);
        log.Add($"{Kind} {Xref}: deleted ({linksRemoved} link(s) removed)");
    }

    private static int RemoveLinks(ApplyState state, string xref)
    {
        int removed = 0;
        foreach (var root in state.Doc.Records)
            removed += RemoveLinksRecursive(state, root, xref);
        return removed;
    }

    private static int RemoveLinksRecursive(ApplyState state, GedRecord node, string xref)
    {
        var links = node.Children.Where(c => c.Tag == "OBJE" && c.Value == xref).ToList();
        foreach (var link in links) node.Children.Remove(link);
        if (links.Count > 0) { state.Mutated(); state.Touch(node); }

        int removed = links.Count;
        foreach (var child in node.Children.ToList())
            removed += RemoveLinksRecursive(state, child, xref);
        return removed;
    }
}

/// <summary>
/// Resolvers shared by <see cref="CreateOrUpdateMediaOp"/>'s validation and
/// application, kept alongside <see cref="Resolve"/>'s pattern: both phases
/// derive the same create-vs-update decision, recomputed fresh at apply time
/// against the live document rather than carried on the op instance.
/// </summary>
internal static class MediaResolve
{
    private static readonly Regex MediaXrefNumber = new(@"^@M(\d+)@$", RegexOptions.Compiled);

    /// <summary>The existing OBJE record whose file paths (unescaped) exactly match the requested set, if any.</summary>
    public static GedRecord? FindByFiles(ResolutionContext ctx, IReadOnlyList<MediaFileRequest> files)
    {
        var wanted = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        return ctx.RecordsOfType("OBJE").FirstOrDefault(rec => wanted.SetEquals(
            rec.ChildrenByTag("FILE").Select(f => MediaPaths.UnescapeFilePath(f.FullValue()))));
    }

    /// <summary>
    /// The next unused <c>@M…@</c> xref, 5-digit zero-padded, accounting for
    /// both existing OBJE records and xrefs earlier ops in this run plan to
    /// create (<see cref="ResolutionContext.Planned"/>) — so two omitted-xref
    /// media ops in one changeset don't collide.
    /// </summary>
    public static string NextFreeXref(ResolutionContext ctx)
    {
        int max = ctx.RecordsOfType("OBJE").Select(r => r.Xref ?? "")
            .Concat(ctx.Planned)
            .Select(x => MediaXrefNumber.Match(x))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();
        return $"@M{(max + 1).ToString().PadLeft(5, '0')}@";
    }
}

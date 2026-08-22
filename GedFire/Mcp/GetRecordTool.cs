using System.Text.Json;
using GedCore;
using GedCore.Apply;
using GedFire.Gen;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The get_record MCP tool (docs/design/mcp-server.md "The third tool:
// get_record"). Declares the tool's metadata and schemas verbatim from that
// addendum; trims and validates xref; obtains the snapshot from
// DocumentSession; looks the xref up across Individuals/Families/Sources and
// maps whichever resolves. No matching or scoring logic — there is none to
// have, only a dictionary lookup.
// ---------------------------------------------------------------------------

public sealed class GetRecordTool
{
    public const string ToolName = "get_record";

    public const string Description =
        "Return everything this document records about one person, family, or source, identified by its local " +
        "xref — the identity, every event with its citations and attached media, notes, and every related " +
        "record's xref for a follow-up call. Call this once a specific person or family is no longer ambiguous " +
        "(after find_person returns a single match, or a candidate the user picked) or when the user's question " +
        "needs detail find_person deliberately omits: children, notes, media, or citations. Pass any xref this " +
        "server has returned — from find_person's person or family fields, or from an earlier get_record's own " +
        "references — never one the user typed from memory. Every media file's \"resolved\" flag tells you " +
        "whether its \"path\" is ready to use as-is: true means open or display it directly (a local absolute " +
        "path, or an external URL to render as a link or image — never fetch or search for it, this server " +
        "does not make network requests); false means the file could not be located, so do not guess at where " +
        "it might be.";

    // Verbatim from docs/design/mcp-server.md "The third tool: get_record" — Input.
    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "xref": {
              "type": "string",
              "pattern": "^@[^@]+@$",
              "minLength": 3,
              "description": "A local xref returned by this server, e.g. \"@I00006@\", \"@F00012@\", or \"@S00042@\". Not a value the user would know to type themselves."
            }
          },
          "required": ["xref"]
        }
        """;

    // Verbatim from docs/design/mcp-server.md "The third tool: get_record" — Output.
    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "oneOf": [
            { "$ref": "#/$defs/PersonRecord" },
            { "$ref": "#/$defs/FamilyRecord" },
            { "$ref": "#/$defs/SourceRecord" },
            { "$ref": "#/$defs/NotFoundRecord" }
          ],
          "$defs": {
            "EventDetail": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "properties": {
                "date": { "type": ["string", "null"] },
                "year": { "type": ["integer", "null"] },
                "qualifier": { "type": ["string", "null"] },
                "place": { "type": ["string", "null"] },
                "sources": { "type": "array", "items": { "type": "string" } },
                "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } }
              },
              "required": ["date", "year", "qualifier", "place", "sources", "media"]
            },
            "NoteDetail": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "text": { "type": "string" },
                "mime": { "type": ["string", "null"] },
                "sources": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["text", "mime", "sources"]
            },
            "MediaFile": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "path": { "type": "string" },
                "mediaType": { "type": "string" },
                "medium": { "type": ["string", "null"] },
                "title": { "type": ["string", "null"] },
                "resolved": { "type": "boolean" }
              },
              "required": ["path", "mediaType", "medium", "title", "resolved"]
            },
            "Crop": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "properties": {
                "top": { "type": ["integer", "null"] },
                "left": { "type": ["integer", "null"] },
                "height": { "type": ["integer", "null"] },
                "width": { "type": ["integer", "null"] }
              },
              "required": ["top", "left", "height", "width"]
            },
            "MediaDetail": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "title": { "type": ["string", "null"] },
                "crop": { "$ref": "#/$defs/Crop" },
                "files": { "type": "array", "items": { "$ref": "#/$defs/MediaFile" } }
              },
              "required": ["xref", "title", "crop", "files"]
            },
            "ChildIdentity": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "name": { "type": "string" },
                "birthYear": { "type": ["integer", "null"] }
              },
              "required": ["xref", "name", "birthYear"]
            },
            "ParentFamilyReference": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "fatherName": { "type": ["string", "null"] },
                "motherName": { "type": ["string", "null"] }
              },
              "required": ["xref", "fatherName", "motherName"]
            },
            "SpouseFamilyDetail": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "spouseName": { "type": ["string", "null"] },
                "marriage": { "$ref": "#/$defs/EventDetail" },
                "children": { "type": "array", "items": { "$ref": "#/$defs/ChildIdentity" } }
              },
              "required": ["xref", "spouseName", "marriage", "children"]
            },
            "SpouseReference": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "properties": {
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "name": { "type": "string" }
              },
              "required": ["xref", "name"]
            },
            "PersonRecord": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "recordType": { "const": "person" },
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "name": { "type": "string" },
                "title": { "type": ["string", "null"] },
                "sex": { "type": ["string", "null"], "enum": ["M", "F", null] },
                "birth": { "$ref": "#/$defs/EventDetail" },
                "death": { "$ref": "#/$defs/EventDetail" },
                "will": { "$ref": "#/$defs/EventDetail" },
                "probate": { "$ref": "#/$defs/EventDetail" },
                "census": { "type": "array", "items": { "$ref": "#/$defs/EventDetail" } },
                "nameSources": { "type": "array", "items": { "type": "string" } },
                "notes": { "type": "array", "items": { "$ref": "#/$defs/NoteDetail" } },
                "restriction": { "type": ["string", "null"] },
                "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } },
                "familyAsChild": { "$ref": "#/$defs/ParentFamilyReference" },
                "familiesAsSpouse": { "type": "array", "items": { "$ref": "#/$defs/SpouseFamilyDetail" } }
              },
              "required": [
                "recordType", "xref", "name", "title", "sex", "birth", "death", "will",
                "probate", "census", "nameSources", "notes", "restriction", "media",
                "familyAsChild", "familiesAsSpouse"
              ]
            },
            "FamilyRecord": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "recordType": { "const": "family" },
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "husband": { "$ref": "#/$defs/SpouseReference" },
                "wife": { "$ref": "#/$defs/SpouseReference" },
                "marriage": { "$ref": "#/$defs/EventDetail" },
                "children": { "type": "array", "items": { "$ref": "#/$defs/ChildIdentity" } },
                "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } }
              },
              "required": ["recordType", "xref", "husband", "wife", "marriage", "children", "media"]
            },
            "SourceRecord": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "recordType": { "const": "source" },
                "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                "author": { "type": ["string", "null"] },
                "title": { "type": ["string", "null"] },
                "publication": { "type": ["string", "null"] },
                "note": { "type": ["string", "null"] }
              },
              "required": ["recordType", "xref", "author", "title", "publication", "note"]
            },
            "NotFoundRecord": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "recordType": { "const": "not_found" },
                "xref": { "type": "string" }
              },
              "required": ["recordType", "xref"]
            }
          }
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly string _mediaDir;

    public GetRecordTool(DocumentSession session, ToolGate gate, string mediaDir)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        if (string.IsNullOrEmpty(mediaDir)) throw new ArgumentException("Media directory must not be empty.", nameof(mediaDir));
        _mediaDir = mediaDir;
    }

    /// <summary>
    /// Build the SDK's McpServerTool for this instance, then overwrite the
    /// advertised description/schemas/annotations with this class's
    /// hand-written, doc-verbatim constants — see FindPersonTool.ToMcpServerTool
    /// for why (the SDK's reflection-derived schema is not the contract).
    /// </summary>
    public McpServerTool ToMcpServerTool()
    {
        var createOptions = new McpServerToolCreateOptions
        {
            Name = ToolName,
            Description = Description,
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
        };

        var tool = McpServerTool.Create(InvokeAsync, createOptions);
        tool.ProtocolTool.Description = Description;
        tool.ProtocolTool.InputSchema = JsonDocument.Parse(InputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse(OutputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
        };
        return tool;
    }

    // The delegate McpServerTool.Create binds arguments to and invokes.
    Task<CallToolResult> InvokeAsync(string xref, CancellationToken cancellationToken = default)
        => HandleAsync(xref, cancellationToken);

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as FindPersonTool.HandleAsync.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(string xref, CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(xref, ct), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CallToolResults.Error($"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    async Task<CallToolResult> ExecuteAsync(string xref, CancellationToken cancellationToken)
    {
        string trimmed = (xref ?? "").Trim();
        if (trimmed.Length == 0)
            return CallToolResults.Error("xref must not be blank.");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var model = snapshot.Model;

        object result =
            model.Individuals.TryGetValue(trimmed, out var indi) ? MapPerson(indi) :
            model.Families.TryGetValue(trimmed, out var fam) ? MapFamily(fam) :
            model.Sources.TryGetValue(trimmed, out var src) ? MapSource(src) :
            new NotFoundRecord("not_found", trimmed);

        return CallToolResults.Success(result, CallToolResults.JsonOptions);
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
        MapSources(indi.NameSources),
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
            MapSources(ev.Sources),
            MapMediaList(ev.Media));
    }

    static NoteDetail MapNote(GedNarrativeNote note) =>
        new(note.Text, note.Mime, MapSources(note.Sources));

    static List<string> MapSources(IEnumerable<GedSourceRef> sourceRefs) =>
        [.. sourceRefs
            .Where(s => s.GlobalSource != null)
            .Select(s => s.GlobalSource!.Xref)];

    List<MediaDetail> MapMediaList(IEnumerable<GedMediaLink> links) =>
        [.. links.Select(MapMedia)];

    MediaDetail MapMedia(GedMediaLink link) => new(
        link.Target.Xref,
        OrNull(link.DisplayTitle),
        MapCrop(link.Crop),
        [.. link.Target.Files.Select(MapMediaFile)]);

    // Resolves a raw GEDCOM FILE payload the same way SiteGenerator's
    // ResolveMediaSrc does for HTML generation (docs/design/mcp-server.md
    // "The third tool: get_record" — the paragraph on MediaFile.resolved):
    // an absolute URL passes through unchanged; a relative path becomes an
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

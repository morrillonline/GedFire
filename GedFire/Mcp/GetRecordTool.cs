using System.Text.Json;
using GedCore;
using GedCore.Apply;
using GedFire.Gen;
using GedFire.Match;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The get_record MCP tool. Declares the tool's metadata and schemas; trims
// and validates xref; obtains the snapshot from DocumentSession; looks the
// xref up across Individuals/Families/Sources and maps whichever resolves.
// No matching or scoring logic — there is none to have, only a dictionary
// lookup.
// ---------------------------------------------------------------------------

public sealed class GetRecordTool
{
    public const string ToolName = "get_record";

    public const string Description =
        "Return everything this document records about one person, family, or source, identified by its local " +
        "xref — the identity, every event with its citations and attached media, notes, and every related " +
        "record's xref for a follow-up call. Call this once a specific person or family is no longer ambiguous " +
        "(after find_person returns a single match, or a candidate the user picked) or when the user's question " +
        "needs detail find_person deliberately omits: children, notes, media, or citations. Each citation gives " +
        "its source xref, page, quoted text (dataText), and quality (quay); set includeSources to also receive " +
        "the full text of every source the record cites. Looking up a source xref directly also lists every " +
        "structure that cites it (citedBy). Pass any xref this " +
        "server has returned — from find_person's person or family fields, or from an earlier get_record's own " +
        "references — never one the user typed from memory. Every media file's \"resolved\" flag tells you " +
        "whether its \"path\" is ready to use as-is: true means open or display it directly (a local absolute " +
        "path, or an external URL to render as a link or image — never fetch or search for it, this server " +
        "does not make network requests); false means the file could not be located, so do not guess at where " +
        "it might be.";

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
            },
            "includeSources": {
              "type": "boolean",
              "default": false,
              "description": "When true and the xref is a person or family, add a sources array with the full SourceRecord (author, title, publication, note) of every source cited on that record, each once. Citations on related records are not followed."
            }
          },
          "required": ["xref"]
        }
        """;

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
                "citations": { "type": "array", "items": { "$ref": "#/$defs/CitationDetail" } },
                "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } }
              },
              "required": ["date", "year", "qualifier", "place", "citations", "media"]
            },
            "NoteDetail": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "text": { "type": "string" },
                "mime": { "type": ["string", "null"] },
                "citations": { "type": "array", "items": { "$ref": "#/$defs/CitationDetail" } }
              },
              "required": ["text", "mime", "citations"]
            },
            "CitationDetail": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "kind": { "enum": ["citation", "inlineNote", "personalNote"], "description": "citation: a real source citation. inlineNote: prose carried on the citation. personalNote: the personal-note pseudo-source, which cites nothing." },
                "source": { "type": ["string", "null"], "description": "The cited source's xref; null only for an inline note that names no source." },
                "page": { "type": ["string", "null"] },
                "dataText": { "type": ["string", "null"], "description": "The quoted or transcribed text recorded with the citation." },
                "quay": { "type": ["integer", "null"], "minimum": 0, "maximum": 3, "description": "Citation quality, 0 (unreliable) through 3 (direct evidence)." }
              },
              "required": ["kind", "source", "page", "dataText", "quay"]
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
                "otherEvents": {
                  "type": "array",
                  "description": "Other individual events (BURI, BAPM, CHR, CREM, ...), each named by its GEDCOM tag.",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "tag": { "type": "string" },
                      "date": { "type": ["string", "null"] },
                      "year": { "type": ["integer", "null"] },
                      "qualifier": { "type": ["string", "null"] },
                      "place": { "type": ["string", "null"] },
                      "citations": { "type": "array", "items": { "$ref": "#/$defs/CitationDetail" } },
                      "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } }
                    },
                    "required": ["tag", "date", "year", "qualifier", "place", "citations", "media"]
                  }
                },
                "nameCitations": { "type": "array", "items": { "$ref": "#/$defs/CitationDetail" } },
                "notes": { "type": "array", "items": { "$ref": "#/$defs/NoteDetail" } },
                "restriction": { "type": ["string", "null"] },
                "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } },
                "familyAsChild": { "$ref": "#/$defs/ParentFamilyReference" },
                "familiesAsSpouse": { "type": "array", "items": { "$ref": "#/$defs/SpouseFamilyDetail" } },
                "sources": { "type": "array", "items": { "$ref": "#/$defs/SourceRecord" }, "description": "Only with includeSources." }
              },
              "required": [
                "recordType", "xref", "name", "title", "sex", "birth", "death", "will",
                "probate", "census", "otherEvents", "nameCitations", "notes", "restriction", "media",
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
                "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } },
                "sources": { "type": "array", "items": { "$ref": "#/$defs/SourceRecord" }, "description": "Only with includeSources." }
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
                "note": { "type": ["string", "null"] },
                "citedBy": {
                  "type": "array",
                  "description": "Only on a direct lookup of the source: each structure that cites it.",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                      "recordType": { "enum": ["person", "family"] },
                      "field": { "enum": ["name", "birth", "death", "will", "probate", "census", "otherEvent", "note", "marriage"] }
                    },
                    "required": ["xref", "recordType", "field"]
                  }
                }
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
    readonly RecordMapper _mapper;

    public GetRecordTool(DocumentSession session, ToolGate gate, string mediaDir)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _mapper = new RecordMapper(mediaDir);
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
    // includeSources arrives as a raw JsonElement so a wrong type is reported
    // by name instead of failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        string xref, JsonElement? includeSources = null, CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryReadOptionalBool(includeSources ?? default, "includeSources", out bool include, out string? error))
            return Task.FromResult(CallToolResults.Error(error!));
        return HandleAsync(xref, cancellationToken, include);
    }

    /// <summary>
    /// The tool's actual behavior, reachable directly without any MCP
    /// protocol machinery: admission through ToolGate, then the work itself.
    /// Never throws: every failure becomes an isError CallToolResult, the
    /// same last-chance-handler pattern as FindPersonTool.HandleAsync.
    /// </summary>
    public async Task<CallToolResult> HandleAsync(string xref, CancellationToken cancellationToken, bool includeSources = false)
    {
        try
        {
            return await _gate.RunAsync(ct => ExecuteAsync(xref, includeSources, ct), cancellationToken).ConfigureAwait(false);
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

    async Task<CallToolResult> ExecuteAsync(string xref, bool includeSources, CancellationToken cancellationToken)
    {
        string trimmed = (xref ?? "").Trim();
        if (trimmed.Length == 0)
            return CallToolResults.Error("xref must not be blank.");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        object result = _mapper.Map(snapshot.Model, trimmed, includeSources);

        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }
}

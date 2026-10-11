using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Drafting;
using GedCore.Policy;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The draft_changeset MCP tool: compiles findings into a changeset in the op
// dialect so the mechanical parts (source reuse, merged extracts, verbatim
// locators, verification notes, QUAY ceilings) are written the same way every
// time. The author supplies the judgement: each fact's value and grade, and
// any note wording. Never touches the GEDCOM file; validate_changeset and
// apply_changeset remain the only tools that read or write a changeset's effect.
// ---------------------------------------------------------------------------

public sealed class DraftChangesetTool
{
    public const string ToolName = "draft_changeset";

    public const string Description =
        "Compile findings into a changeset in the op dialect, so the mechanical parts are written the same way every " +
        "time: one source record per collection (an existing source with the same title and author is cited instead " +
        "of minted), one merged extract per (source, page, grade) in each item, the locator copied verbatim into the " +
        "citation page, a verification note on every record holding a fact graded at or below the policy ceiling " +
        "(QUAY 1 without a policy file), and a refusal, naming the field, for a QUAY the source and information " +
        "class cannot support, a target name that differs from the person's recorded name, or prose the server's " +
        "policy bans. You supply the judgement: each fact's record, tag, date, place and QUAY, and any note text. " +
        "Findings aimed at the same target become one item. Returns the changeset, a decisions list (what was reused, " +
        "minted, merged, or left open for you), and errors; with outPath it also writes the changeset to a new file, " +
        "never overwriting one. Reads the document for existing sources and names, writes nothing to it; run " +
        "validate_changeset on the result.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "findings": {
              "type": "array",
              "minItems": 1,
              "description": "The findings to compile. Unrecognized fields are rejected.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "id": { "type": "string", "description": "The finding's identifier, echoed in decisions." },
                  "extract": { "type": "string", "description": "The verbatim extract cited as the citation's dataText unless a fact gives its own excerpt." },
                  "source": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "name": { "type": "string", "description": "The collection, e.g. \"Find a Grave\"; one source record is made per name and author." },
                      "author": { "type": "string" },
                      "url": { "type": "string", "description": "Written on the source record only when every finding of the collection gives the same one." },
                      "locator": { "type": "string", "description": "Copied verbatim into each citation's page." },
                      "accessed": { "type": "string" },
                      "class": { "type": "string", "enum": ["original", "image copy", "derivative", "authored", "user-contributed"] },
                      "information": { "type": "string", "enum": ["primary", "secondary", "undetermined"] }
                    },
                    "required": ["name", "locator", "class", "information"]
                  },
                  "target": {
                    "type": "object",
                    "additionalProperties": false,
                    "description": "The person the work is for; findings with the same xref and card become one item.",
                    "properties": {
                      "xref": { "type": "string", "pattern": "^@[^@]+@$" },
                      "card": { "type": "string" },
                      "name": { "type": "string", "description": "Checked against the name recorded for the xref." }
                    },
                    "required": ["xref"]
                  },
                  "facts": {
                    "type": "array",
                    "description": "The facts to assert, each graded on its own.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "record": { "type": "string", "pattern": "^@[^@]+@$", "description": "The person or family the fact belongs to." },
                        "fact": { "type": "string", "description": "The GEDCOM tag, e.g. BIRT, DEAT, BURI, MARR." },
                        "date": { "type": "string" },
                        "place": { "type": "string" },
                        "quay": { "type": "integer", "minimum": 0, "maximum": 3 },
                        "class": { "type": "string", "enum": ["original", "image copy", "derivative", "authored", "user-contributed"], "description": "Overrides the source's class for this fact." },
                        "information": { "type": "string", "enum": ["primary", "secondary", "undetermined"], "description": "Overrides the source's information class for this fact." },
                        "excerpt": { "type": "string", "description": "A narrower extract for this fact's citation." }
                      },
                      "required": ["record", "fact", "quay"]
                    }
                  },
                  "notes": {
                    "type": "array",
                    "description": "Notes you wrote, passed through unchanged and checked against the policy.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "record": { "type": "string", "pattern": "^@[^@]+@$" },
                        "text": { "type": "string" }
                      },
                      "required": ["record", "text"]
                    }
                  }
                },
                "required": ["id", "extract", "source", "target"]
              }
            },
            "outPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Where to write the changeset. Refused when the file already exists or the draft has errors."
            }
          },
          "required": ["findings"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean", "description": "false when errors is non-empty; the changeset is then not written." },
            "changeset": { "type": ["object", "null"], "description": "The drafted changeset; null when the findings could not be compiled." },
            "decisions": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "kind": { "type": "string", "enum": ["reused-source", "minted-source", "merged-citation", "verification-note", "open"] },
                  "detail": { "type": "string" },
                  "findings": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["kind", "detail", "findings"]
              }
            },
            "errors": { "type": "array", "items": { "type": "string" } },
            "writtenTo": { "type": ["string", "null"], "description": "The full path written, or null." }
          },
          "required": ["success", "changeset", "decisions", "errors", "writtenTo"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly ChangesetPolicy? _policy;

    public DraftChangesetTool(DocumentSession session, ToolGate gate, ChangesetPolicy? policy = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _policy = policy;
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.CreatesFiles)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by
    // field name rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? findings = null, JsonElement? outPath = null, CancellationToken cancellationToken = default)
        => HandleAsync(findings ?? default, outPath ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(JsonElement findings, JsonElement outPath, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(findings, outPath, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(JsonElement findings, JsonElement outPath, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryReadOptionalString(outPath, "outPath", out string? path, out string? error))
            return CallToolResults.Error(error!);
        if (path is not null && string.IsNullOrWhiteSpace(path))
            return CallToolResults.Error("outPath must not be blank.");

        var (parsed, readErrors) = DraftFindingReader.Read(findings);
        if (readErrors.Count > 0)
            return Respond(new DraftResult(null, [], readErrors), null);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var draft = new ChangesetDrafter(new ModelDraftLookup(snapshot.Model), _policy).Draft(parsed);

        string? writtenTo = null;
        if (path is not null && draft.Success)
        {
            var written = TryWrite(Path.GetFullPath(path.Trim()), draft.Changeset!, out string? writeError);
            if (writeError is not null) return Respond(draft with { Errors = [writeError] }, null);
            writtenTo = written;
        }
        return Respond(draft, writtenTo);
    }

    static string? TryWrite(string fullPath, JsonObject changeset, out string? error)
    {
        error = null;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            changeset.WriteTo(writer);
            return fullPath;
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            error = $"outPath {fullPath} already exists; draft_changeset never overwrites a file.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"Could not write {fullPath}: {ex.Message}";
        }
        return null;
    }

    static CallToolResult Respond(DraftResult draft, string? writtenTo) =>
        CallToolResults.Success(
            new DraftChangesetResult(draft.Success, draft.Changeset, draft.Decisions, draft.Errors, writtenTo),
            CallToolResults.JsonOptions);
}

public sealed record DraftChangesetResult(
    bool Success, JsonObject? Changeset, IReadOnlyList<DraftDecision> Decisions, IReadOnlyList<string> Errors, string? WrittenTo);

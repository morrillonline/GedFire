using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Drafting;
using GedCore.Policy;
using GedFire.Schemas;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The draft_changeset MCP tool: compiles findings into a changeset in the op
// dialect so the mechanical parts (source reuse, merged extracts, verbatim
// locators, QUAY ceilings) are written the same way every
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
                "of minted; titles match ignoring case and punctuation, and a trailing \" - <person name>\" is dropped, so " +
                "record-specific text belongs in the locator; source.ref names the existing source to cite when several share a title " +
                "and author, and is refused unless it is a source whose title and author agree), one merged extract per (source, page, grade) in each item, the locator copied verbatim into the " +
        "citation page (a finding without a locator only adds, through citeOnly facts, to a held citation of its source that has no page); and a refusal, naming the field, for a QUAY the source and information " +
        "class cannot support, a target name that differs from the person's recorded name, or prose the server's " +
        "policy bans. It never writes a note of its own: a person's notes hold only their life story, as a Register-style report would show it. " +
        "You supply the judgement: each fact's record, tag, date, place (or a text value such as a NAME " +
        "change) and QUAY, and any note text. A repeatable tag (CENS, RESI, ...) is placed by date: a date no instance " +
        "the record holds has is added (\"mode\":\"add\", even on a record with none), a date one instance has updates it " +
        "by match (a bare year names a fuller date in that year), and a date several have is refused, naming them. " +
        "A spouse relation between two people who already share a family compiles onto that family instead of a new " +
        "one (a resolved decision says so); naming another existing family, or a couple who share several, is refused. " +
        "New people and families come from people " +
        "(placeholder xrefs) and relations (spouse, child, parent), compiled to the dialect's relationship ops with " +
        "the finding's citation. NAME facts may correct the target or people bound by existingPeople or relations; " +
        "a fact's record alone is not a binding. Surname changes require allowSurnameChange=true. A NAME correction " +
        "removes the previous name and its citations; it writes no note about them. Relations use " +
        "the finding's citation. A new parent, spouse or child is refused when the person it attaches to already has a " +
        "parent in that role, or a spouse or child of an agreeing name; the error names that relative's xref, to use in " +
        "place of the placeholder or to list in the relation's notDuplicateOf. A note may rewrite an existing note (match), delete it (match and withdraw, a reason), " +
        "and cite findings (cites); a fact may cite other findings too (cites), keeping its own citation. A cite may name any " +
        "finding, whatever its target: one of the draft, or with findingPaths one staged as <id>.json beside the citing " +
        "file, which lends only its source, locator and extract and is not drafted; " +
        "a biography note must cite at least one; " +
        "a fact may carry substructures, a replacedCitations disposition, citeOnly to cite a fact already held, " +
        "commentary (a sentence or two on its record's evidence, written on its citation and printed in the fact's footnote; " +
        "every cite, on a fact or a note, takes its own; evidence discussion belongs in commentary, a note holds the life story), " +
        "commentaryOnly with commentary to add only a comment citing no source (SOUR @VOID@) to a fact already held, with no quay, " +
        "replaceHeld (a reason) to write over the record's value, or withdraw (a reason) to delete it. A spouse, child or parent " +
        "relation takes cites and commentary the same way, on its family's citations, and commentaryOnly on a link the tree " +
        "already holds. A record's date gives " +
        "way to a finding's date or bound that contradicts it only when uncited or every citation is graded below the finding; " +
        "a citation without a QUAY holds it. Otherwise the record's date is kept and an open decision names it, its citations " +
        "(source and page) and the finding's conflicting date, so you can write a conflict note or pass replaceHeld; a more " +
        "detailed place from the finding is still added beside it. Hard bounds " +
        "(BEF, AFT, BET) narrow one another into one range. " +
        "Findings aimed at the same target become one item. Returns the changeset, a " +
        "decisions list (what was reused, minted, merged, or left open for you), and errors; the changeset follows the " +
        "changeset contract (describe_schemas, changeset): it gives every citation in a " +
        "citations array (familyCitations on a relationship op), and declares each new person once in its item's people. " +
        "It also carries a " +
        "findings table (each finding's extract, source, classes, grades and identity argument, carried unchanged) and each op names the findings it rests on, " +
        "which is what draft_package presents; with outPath it also " +
        "writes the changeset to a new file, replacing an existing one only when overwrite is true. Findings may be " +
        "given inline or by inputPath, a file holding {\"findings\": [...]}, to re-draft a saved input without " +
        "passing it back, or by findingPaths, finding files (see describe_schemas, finding-file) whose \"draft\" entries are compiled " +
        "as one set. Pass reportPath to write the full result JSON to a file a script can read; the response is then " +
        "only a digest (per item its number, target and op count, plus the decisions counted by kind), unless verbose " +
        "is true. Reads the " +
        "document for existing sources and names, writes nothing to it; run validate_changeset on the result.";

    public static readonly string InputSchemaJson = SchemaComposer.Compose("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "findings": {
              "type": "array",
              "minItems": 1,
              "description": "The findings to compile; required unless inputPath or findingPaths is given. Unrecognized fields are rejected.",
              "items": {}
            },
            "inputPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Path to a JSON file holding {\"findings\": [...]}, the same array as findings, so a saved input can be re-drafted without passing it back inline. Give findings or inputPath, not both."
            },
            "findingPaths": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Paths to finding files (gedfire schema finding-file). Each holds the finding's complete entry under \"draft\" (the same shape as one findings item); an optional \"argument\" object beside \"draft\" holds the finding's identity argument, which is carried unchanged into the changeset. The caller's own \"meta\" and the \"record\" slot are never compiled; any other top-level key, or a draft.id that differs from the file's name (<id>.json), is refused. The entries are compiled as one set and the notes several findings of one item wrote on the same record are joined. A file with no draft or a non-empty draftGaps is reported by name instead of being compiled. Give findingPaths, or findings or inputPath, not both."
            },
            "outPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Where to write the changeset. Refused when it is this server's GEDCOM file, when the file already exists, unless overwrite is true, or when the draft has errors."
            },
            "reportPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Write the full result JSON to this file and return only a digest inline (pass verbose: true for both), so a script can read the exact result without it also filling the caller's context. The folder must exist. Refused, before any work is done, when it is this server's GEDCOM file, when the file already exists unless overwrite is true, or when it is outPath."
            },
            "overwrite": {
              "type": "boolean",
              "description": "Replace an existing file at outPath or reportPath. Default false."
            },
            "verbose": {
              "type": "boolean",
              "description": "With reportPath, return the full result inline as well as writing the file, instead of the digest. Default false; ignored without reportPath."
            }
          }
        }
        """, schema => schema["properties"]!["findings"]!["items"] = JsonNode.Parse(FindingSchemas.FindingJson));

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "description": "The full result, or -- when reportPath was given without verbose -- a digest, with the full result in the report file.",
          "properties": {
            "success": { "type": "boolean", "description": "false when errors is non-empty; the changeset is then not written." },
            "changeset": { "type": ["object", "null"], "description": "The drafted changeset; null when the findings could not be compiled. Absent from the digest; the report file holds it." },
            "decisions": {
              "type": "array",
              "description": "Absent from the digest; the report file holds it.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "kind": { "type": "string", "enum": ["reused-source", "minted-source", "minted-person", "merged-citation", "kept-citation", "dropped-seed", "resolved", "open"] },
                  "detail": { "type": "string" },
                  "findings": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["kind", "detail", "findings"]
              }
            },
            "items": {
              "type": "array",
              "description": "Digest only: one entry per drafted item -- its number, its target, and how many ops it carries.",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "item": { "type": "integer" },
                  "target": { "type": "string" },
                  "ops": { "type": "integer" }
                },
                "required": ["item", "target", "ops"]
              }
            },
            "decisionsSummary": { "type": "string", "description": "Digest only: the decisions counted by kind; the report file holds each one." },
            "errors": { "type": "array", "items": { "type": "string" } },
            "writtenTo": { "type": ["string", "null"], "description": "The full path written, or null." },
            "reportWrittenTo": { "type": ["string", "null"], "description": "The full path the result was also written to when reportPath was given, else null." }
          },
          "required": ["success", "errors", "writtenTo", "reportWrittenTo"]
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
        JsonElement? findings = null, JsonElement? inputPath = null, JsonElement? findingPaths = null,
        JsonElement? outPath = null, JsonElement? overwrite = null, JsonElement? reportPath = null,
        JsonElement? verbose = null, CancellationToken cancellationToken = default)
        => HandleAsync(
            new DraftChangesetArguments(
                findings ?? default, inputPath ?? default, findingPaths ?? default, outPath ?? default, overwrite ?? default,
                reportPath ?? default, verbose ?? default),
            cancellationToken);

    public Task<CallToolResult> HandleAsync(JsonElement findings, JsonElement outPath, JsonElement overwrite, CancellationToken cancellationToken) =>
        HandleAsync(new DraftChangesetArguments(findings, default, default, outPath, overwrite), cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement findings, JsonElement inputPath, JsonElement outPath, JsonElement overwrite, CancellationToken cancellationToken) =>
        HandleAsync(new DraftChangesetArguments(findings, inputPath, default, outPath, overwrite), cancellationToken);

    public Task<CallToolResult> HandleAsync(DraftChangesetArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(arguments, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(DraftChangesetArguments arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryReadOptionalString(arguments.OutPath, "outPath", out string? path, out string? error))
            return CallToolResults.Error(error!);
        if (!ToolArguments.TryReadOptionalBool(arguments.Overwrite, "overwrite", out bool replace, out error))
            return CallToolResults.Error(error!);
        if (path is not null && string.IsNullOrWhiteSpace(path))
            return CallToolResults.Error("outPath must not be blank.");
        if (path is not null && GedcomOverwriteGuard.Refusal(path.Trim(), "outPath", _session.DocumentPath) is { } refusal)
            return CallToolResults.Error(refusal);
        if (!ReportFile.TryRead(arguments.ReportPath, arguments.Overwrite, arguments.Verbose, _session.DocumentPath, out var report, out error))
            return CallToolResults.Error(error!);
        if (report is not null && path is not null && Path.GetFullPath(path.Trim()) == report.FullPath)
            return CallToolResults.Error("reportPath must differ from outPath.");

        var input = ReadFindings(arguments, out error);
        if (error is not null) return CallToolResults.Error(error);
        if (input.Errors.Count > 0)
            return Respond(new DraftResult(null, [], input.Errors), null, report);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var draft = new ChangesetDrafter(new ModelDraftLookup(snapshot.Model), _policy).Draft(input.Findings, input.Cited);
        if (input.Files.Count > 0) draft = draft with { Errors = FindingFileLoader.NameFiles(draft.Errors, input.Files) };

        string? writtenTo = null;
        if (path is not null && draft.Success)
        {
            var written = TryWrite(Path.GetFullPath(path.Trim()), draft.Changeset!, replace, out string? writeError);
            if (writeError is not null) return Respond(draft with { Errors = [writeError] }, null, report);
            writtenTo = written;
        }
        return Respond(draft, writtenTo, report);
    }

    // Files names the file each finding came from when they came from findingPaths, so a check can name it; Cited holds the
    // findings staged beside those files that they cite, which are cited but not drafted.
    sealed record FindingInput(IReadOnlyList<DraftFinding> Findings, IReadOnlyList<string> Errors, IReadOnlyList<string> Files)
    {
        public IReadOnlyList<DraftFinding> Cited { get; init; } = [];
    }

    // The findings come from exactly one of: the findings argument, the inputPath file, or the finding files.
    static FindingInput ReadFindings(DraftChangesetArguments arguments, out string? error)
    {
        error = null;
        bool hasPaths = arguments.FindingPaths.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (hasPaths)
        {
            bool hasOther = arguments.Findings.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
                || arguments.InputPath.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
            if (hasOther)
            {
                error = "give findingPaths, or findings or inputPath, not both.";
                return new FindingInput([], [], []);
            }

            var loaded = FindingFileLoader.Load(arguments.FindingPaths);
            return new FindingInput(loaded.Findings, loaded.Errors, loaded.Files) { Cited = loaded.Cited };
        }

        if (!ArgumentFile.TryResolve(arguments.Findings, "findings", arguments.InputPath, "inputPath", out var findings, out error))
            return new FindingInput([], [], []);
        var (read, readErrors) = DraftFindingReader.Read(findings);
        return new FindingInput(read, readErrors, []);
    }

    static string? TryWrite(string fullPath, JsonObject changeset, bool replace, out string? error) =>
        ChangesetFileWriter.TryWrite(fullPath, ChangesetFileWriter.Serialize(changeset), replace, out error);

    // The result is also written to the report file when one was asked for. The report never changes the outcome: a write
    // that fails leaves reportWrittenTo null and says so in an open decision. A written report quiets the response to a
    // digest of the result, unless verbose asks for both.
    static CallToolResult Respond(DraftResult draft, string? writtenTo, ReportRequest? report)
    {
        var result = new DraftChangesetResult(draft.Success, draft.Changeset, draft.Decisions, draft.Errors, writtenTo);
        if (report is null) return CallToolResults.Success(result, CallToolResults.JsonOptions);

        var reported = result with { ReportWrittenTo = report.FullPath };
        if (!ReportFile.TryWrite(reported, report, out string? error))
        {
            result = result with { Decisions = [.. result.Decisions, new DraftDecision(DraftDecisionKinds.Open, $"report not written: {error}", [])] };
            return CallToolResults.Success(result, CallToolResults.JsonOptions);
        }
        return CallToolResults.Success(
            report.Verbose ? reported : DraftChangesetDigest.Of(reported), CallToolResults.JsonOptions);
    }
}

public sealed record DraftChangesetArguments(
    JsonElement Findings, JsonElement InputPath, JsonElement FindingPaths, JsonElement OutPath, JsonElement Overwrite,
    JsonElement ReportPath = default, JsonElement Verbose = default);

public sealed record DraftChangesetResult(
    bool Success, JsonObject? Changeset, IReadOnlyList<DraftDecision> Decisions, IReadOnlyList<string> Errors, string? WrittenTo)
{
    /// <summary>The path the result was also written to when reportPath was given.</summary>
    public string? ReportWrittenTo { get; init; }
}

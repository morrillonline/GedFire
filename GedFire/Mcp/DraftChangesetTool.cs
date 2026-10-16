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
                "of minted; titles match ignoring case and punctuation, and a trailing \" - <person name>\" is dropped, so " +
                "record-specific text belongs in the locator), one merged extract per (source, page, grade) in each item, the locator copied verbatim into the " +
        "citation page, a verification note on every record holding a fact graded at or below the policy ceiling " +
        "(QUAY 1 without a policy file), and a refusal, naming the field, for a QUAY the source and information " +
        "class cannot support, a target name that differs from the person's recorded name, or prose the server's " +
        "policy bans. You supply the judgement: each fact's record, tag, date, place (or a text value such as a NAME " +
        "change) and QUAY, and any note text. A repeatable tag (CENS, RESI, ...) is added beside the instances the " +
        "record already holds, or updates the one with the same date. New people and families come from people " +
        "(placeholder xrefs) and relations (spouse, child, parent), compiled to the dialect's relationship ops with " +
        "the finding's citation. A note may rewrite an existing note (match) and cite findings of its item (cites); " +
        "a fact may carry substructures, a replacedCitations disposition, or citeOnly to cite a fact already held. " +
        "Findings aimed at the same target become one item. Returns the changeset, a " +
        "decisions list (what was reused, minted, merged, or left open for you), and errors; with outPath it also " +
        "writes the changeset to a new file, replacing an existing one only when overwrite is true. Findings may be " +
        "given inline or by inputPath, a file holding {\"findings\": [...]}, to re-draft a saved input without " +
        "passing it back, or by findingPaths, the lake's correlated finding files, whose \"draft\" entries are compiled " +
        "as one set. Pass reportPath to also write the result JSON, exactly as returned, to a file a script can read. Reads the " +
        "document for existing sources and names, writes nothing to it; run validate_changeset on the result.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "findings": {
              "type": "array",
              "minItems": 1,
              "description": "The findings to compile; required unless inputPath or findingPaths is given. Unrecognized fields are rejected.",
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
                        "value": { "type": "string", "description": "A text fact's payload, e.g. a NAME change (\"Ralph /Morrill/\"); not combined with date or place." },
                        "quay": { "type": "integer", "minimum": 0, "maximum": 3 },
                        "class": { "type": "string", "enum": ["original", "image copy", "derivative", "authored", "user-contributed"], "description": "Overrides the source's class for this fact." },
                        "information": { "type": "string", "enum": ["primary", "secondary", "undetermined"], "description": "Overrides the source's information class for this fact." },
                        "excerpt": { "type": "string", "description": "A narrower extract for this fact's citation." },
                        "substructures": {
                          "type": "array",
                          "description": "Extra lines under the fact, e.g. [{\"tag\": \"TYPE\", \"value\": \"Pension Application\"}] on an EVEN. A fact with substructures is added, never matched by date.",
                          "items": {
                            "type": "object",
                            "additionalProperties": false,
                            "properties": { "tag": { "type": "string" }, "value": { "type": "string" } },
                            "required": ["tag", "value"]
                          }
                        },
                        "replacedCitations": { "type": "string", "enum": ["keep", "drop", "moveToNote"], "description": "What becomes of the citations on a fact whose value this replaces; default keep." },
                        "citeOnly": { "type": "boolean", "description": "Only add this finding's citation to a fact the record already holds. date and place then select the instance when the record has several." }
                      },
                      "required": ["record", "fact", "quay"]
                    }
                  },
                  "people": {
                    "type": "array",
                    "description": "New people this finding adds, each named by a placeholder xref. A person is created by the relation that links them; give their facts under facts with the placeholder as record.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "xref": { "type": "string", "pattern": "^@New[A-Za-z0-9_]+@$", "description": "A placeholder such as @NewI1@; apply mints the real xref." },
                        "name": { "type": "string", "description": "GEDCOM form, e.g. \"Sarah /Blake/\"." },
                        "sex": { "type": "string", "enum": ["M", "F"], "description": "Required for a partner, so HUSB and WIFE can be assigned." }
                      },
                      "required": ["xref", "name"]
                    }
                  },
                  "relations": {
                    "type": "array",
                    "description": "Links this finding establishes or, with the unlink kinds, removes, in the changeset dialect's terms. Each added link is cited with the finding's source at its own quay; a child relation with from moves an existing child between families. Family xrefs for new families are placeholders such as @NewF1@; facts about the family (MARR) use that xref as record.",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "properties": {
                        "kind": { "type": "string", "enum": ["spouse", "child", "parent", "unlink-child", "unlink-parent"], "description": "unlink-child and unlink-parent remove an existing link; they take no quay or excerpt, because a removal op carries no citation: say why in a note that cites the finding." },
                        "person": { "type": "string", "description": "spouse, parent and unlink-parent: the known person (the child, for parent and unlink-parent). An existing xref, or a person an earlier relation created." },
                        "spouse": { "type": "string", "description": "spouse: the partner, existing or declared in people." },
                        "child": { "type": "string", "description": "child and unlink-child: the child, existing or declared in people (an existing person for unlink-child, or for a child with from)." },
                        "parent": { "type": "string", "description": "parent: the parent, existing or declared in people." },
                        "role": { "type": "string", "enum": ["father", "mother"], "description": "parent and unlink-parent: which parent." },
                        "family": { "type": "string", "description": "The family: required for spouse, child and unlink-child (the family the child leaves), optional for parent and unlink-parent. A placeholder creates it, except for the removals." },
                        "from": { "type": "string", "description": "child: an existing family the child is moved out of, so the child ends up in family only. The child must be listed in it." },
                        "husb": { "type": "string", "description": "child: father, used only when this relation creates the family." },
                        "wife": { "type": "string", "description": "child: mother, used only when this relation creates the family." },
                        "notDuplicateOf": { "type": "array", "items": { "type": "string" }, "description": "spouse and child: existing people already confirmed not to be the new person." },
                        "excerpt": { "type": "string", "description": "A narrower extract than the finding's for this relation's citation." },
                        "quay": { "type": "integer", "minimum": 0, "maximum": 3, "description": "Required except for unlink-child and unlink-parent." }
                      },
                      "required": ["kind"]
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
                        "text": { "type": "string" },
                        "match": { "type": "string", "description": "The exact text of an existing note on the record, to rewrite instead of adding a new note." },
                        "cites": {
                          "type": "array",
                          "description": "Citations on the note. Each cites a finding of this draft aimed at the same target (the note's own finding when finding is omitted), graded on its own.",
                          "items": {
                            "type": "object",
                            "additionalProperties": false,
                            "properties": {
                              "finding": { "type": "string", "description": "The id of the finding cited; default the note's own." },
                              "quay": { "type": "integer", "minimum": 0, "maximum": 3 },
                              "excerpt": { "type": "string", "description": "A narrower extract than the finding's." }
                            },
                            "required": ["quay"]
                          }
                        }
                      },
                      "required": ["record", "text"]
                    }
                  }
                },
                "required": ["id", "extract", "source", "target"]
              }
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
              "description": "Paths to correlated finding files from the lake. Each holds the finding's complete entry under \"draft\" (the same shape as one findings item); the entries are compiled as one set and the notes several findings of one item wrote on the same record are joined. A file with no draft, a parked finding, or a non-empty draftGaps is reported by name instead of being compiled. Give findingPaths, or findings or inputPath, not both."
            },
            "outPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Where to write the changeset. Refused when the file already exists, unless overwrite is true, or when the draft has errors."
            },
            "reportPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Also write the result JSON, exactly as returned, to this file, so a script can read it without anyone retyping it. The folder must exist. Refused, before any work is done, when the file already exists unless overwrite is true, or when it is outPath."
            },
            "overwrite": {
              "type": "boolean",
              "description": "Replace an existing file at outPath or reportPath. Default false."
            }
          }
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
                  "kind": { "type": "string", "enum": ["reused-source", "minted-source", "minted-person", "merged-citation", "kept-citation", "verification-note", "dropped-seed", "resolved", "open"] },
                  "detail": { "type": "string" },
                  "findings": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["kind", "detail", "findings"]
              }
            },
            "errors": { "type": "array", "items": { "type": "string" } },
            "writtenTo": { "type": ["string", "null"], "description": "The full path written, or null." },
            "reportWrittenTo": { "type": ["string", "null"], "description": "The full path the result was also written to when reportPath was given, else null." }
          },
          "required": ["success", "changeset", "decisions", "errors", "writtenTo", "reportWrittenTo"]
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
        CancellationToken cancellationToken = default)
        => HandleAsync(
            new DraftChangesetArguments(
                findings ?? default, inputPath ?? default, findingPaths ?? default, outPath ?? default, overwrite ?? default,
                reportPath ?? default),
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
        if (!ReportFile.TryRead(arguments.ReportPath, arguments.Overwrite, out var report, out error))
            return CallToolResults.Error(error!);
        if (report is not null && path is not null && Path.GetFullPath(path.Trim()) == report.FullPath)
            return CallToolResults.Error("reportPath must differ from outPath.");

        var input = ReadFindings(arguments, out error);
        if (error is not null) return CallToolResults.Error(error);
        if (input.Errors.Count > 0)
            return Respond(new DraftResult(null, [], input.Errors), null, report);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var draft = new ChangesetDrafter(new ModelDraftLookup(snapshot.Model), _policy).Draft(input.Findings);
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

    // Files names the file each finding came from when they came from findingPaths, so a check can name it.
    sealed record FindingInput(IReadOnlyList<DraftFinding> Findings, IReadOnlyList<string> Errors, IReadOnlyList<string> Files);

    // The findings come from exactly one of: the findings argument, the inputPath file, or the lake's finding files.
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

            FindingFileLoader.TryLoad(arguments.FindingPaths, out var loaded, out var files, out var loadErrors);
            return new FindingInput(loaded, loadErrors, files);
        }

        if (!ArgumentFile.TryResolve(arguments.Findings, "findings", arguments.InputPath, "inputPath", out var findings, out error))
            return new FindingInput([], [], []);
        var (read, readErrors) = DraftFindingReader.Read(findings);
        return new FindingInput(read, readErrors, []);
    }

    static string? TryWrite(string fullPath, JsonObject changeset, bool replace, out string? error) =>
        ChangesetFileWriter.TryWrite(fullPath, ChangesetFileWriter.Serialize(changeset), replace, out error);

    // The result is also written to the report file when one was asked for. The report never changes the outcome: a write
    // that fails leaves reportWrittenTo null and says so in an open decision.
    static CallToolResult Respond(DraftResult draft, string? writtenTo, ReportRequest? report)
    {
        var result = new DraftChangesetResult(draft.Success, draft.Changeset, draft.Decisions, draft.Errors, writtenTo);
        if (report is not null)
        {
            var reported = result with { ReportWrittenTo = report.FullPath };
            if (ReportFile.TryWrite(reported, report, out string? error)) result = reported;
            else result = result with { Decisions = [.. result.Decisions, new DraftDecision(DraftDecisionKinds.Open, $"report not written: {error}", [])] };
        }
        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }
}

public sealed record DraftChangesetArguments(
    JsonElement Findings, JsonElement InputPath, JsonElement FindingPaths, JsonElement OutPath, JsonElement Overwrite,
    JsonElement ReportPath = default);

public sealed record DraftChangesetResult(
    bool Success, JsonObject? Changeset, IReadOnlyList<DraftDecision> Decisions, IReadOnlyList<string> Errors, string? WrittenTo)
{
    /// <summary>The path the result was also written to when reportPath was given.</summary>
    public string? ReportWrittenTo { get; init; }
}

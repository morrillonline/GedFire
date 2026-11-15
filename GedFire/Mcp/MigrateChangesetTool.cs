using System.Text.Json;
using GedCore.Contract;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The migrate_changeset MCP tool: rewrites a changeset written in the legacy
// dialect into the canonical one (see ChangesetMigrator), so a reader of a
// lake of changesets need handle only one shape. Reads and writes changeset
// files only; never reads or writes the GEDCOM document, so it runs on a
// --read-only server and without one.
// ---------------------------------------------------------------------------

public sealed class MigrateChangesetTool
{
    public const string ToolName = "migrate_changeset";

    public const string Description =
        "Rewrite a changeset in the legacy dialect into the canonical one and write it to outPath: a singular citation " +
        "becomes citations (an array); a relationship op's op-level citations become familyCitations; an object value " +
        "becomes date and place beside it; a string fact match becomes {\"value\": ...}; each inline new person moves " +
        "into its item's people, and the op names it by xref; schemaVersion is added. A changeset that gives both " +
        "citation and citations, describes one new person two ways, or does not satisfy the contract at all is refused, " +
        "and nothing is written. Returns each change by path, and the warnings the contract still reports on the " +
        "rewritten file because no rule can fix them (a misspelled field, a date given as an event's value). What an op " +
        "does is unchanged. Never reads or writes the GEDCOM document.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "changesetPath": { "type": "string", "minLength": 1, "pattern": "\\S", "description": "The changeset to migrate." },
            "outPath": { "type": "string", "minLength": 1, "pattern": "\\S", "description": "Where to write the canonical changeset. Refused when the file exists, unless overwrite is true; with overwrite it may be changesetPath itself." },
            "overwrite": { "type": "boolean", "description": "Replace an existing file at outPath. Default false." }
          },
          "required": ["changesetPath", "outPath"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean", "description": "false when errors is non-empty; nothing is then written." },
            "changes": { "type": "array", "items": { "type": "string" }, "description": "Each rewrite, by the path it was made at in the original file." },
            "remaining": { "type": "array", "items": { "type": "string" }, "description": "The contract's warnings on the rewritten file, which need a person to fix." },
            "errors": { "type": "array", "items": { "type": "string" } },
            "schemaVersion": { "type": "string", "description": "The changeset contract version the rewritten file is written to." },
            "writtenTo": { "type": ["string", "null"], "description": "The full path written, or null." }
          },
          "required": ["success", "changes", "remaining", "errors", "schemaVersion", "writtenTo"]
        }
        """;

    readonly ToolGate _gate;

    public MigrateChangesetTool(ToolGate gate) =>
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.CreatesFiles)
            .CreateTool(InvokeAsync);

    Task<CallToolResult> InvokeAsync(
        JsonElement? changesetPath = null, JsonElement? outPath = null, JsonElement? overwrite = null,
        CancellationToken cancellationToken = default) =>
        HandleAsync(changesetPath ?? default, outPath ?? default, overwrite ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement changesetPath, JsonElement outPath, JsonElement overwrite, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, _ => Task.FromResult(Execute(changesetPath, outPath, overwrite)), cancellationToken);

    static CallToolResult Execute(JsonElement changesetPath, JsonElement outPath, JsonElement overwrite)
    {
        if (!ToolArguments.TryReadOptionalString(changesetPath, "changesetPath", out string? source, out string? error)
            || !ToolArguments.TryReadOptionalString(outPath, "outPath", out string? target, out error)
            || !ToolArguments.TryReadOptionalBool(overwrite, "overwrite", out bool replace, out error))
            return CallToolResults.Error(error!);
        if (string.IsNullOrWhiteSpace(source)) return CallToolResults.Error("changesetPath is required.");
        if (string.IsNullOrWhiteSpace(target)) return CallToolResults.Error("outPath is required.");

        string sourcePath = Path.GetFullPath(source.Trim());
        if (!File.Exists(sourcePath)) return CallToolResults.Error($"Changeset file not found: {source}");

        ChangesetMigration migration;
        using (var stream = File.OpenRead(sourcePath))
            migration = ChangesetMigrator.Migrate(stream);
        if (!migration.Success) return Respond(migration, null, []);

        string? written = ChangesetFileWriter.TryWrite(
            Path.GetFullPath(target.Trim()), ChangesetFileWriter.Serialize(migration.Changeset!), replace, out string? writeError);
        return Respond(migration, written, writeError is null ? [] : [writeError]);
    }

    static CallToolResult Respond(ChangesetMigration migration, string? writtenTo, IReadOnlyList<string> writeErrors)
    {
        var errors = migration.Errors.Concat(writeErrors).ToList();
        return CallToolResults.Success(
            new MigrateChangesetResult(errors.Count == 0, migration.Changes, migration.Remaining, errors, ChangesetDialect.SchemaVersion, writtenTo),
            CallToolResults.JsonOptions);
    }
}

public sealed record MigrateChangesetResult(
    bool Success, IReadOnlyList<string> Changes, IReadOnlyList<string> Remaining, IReadOnlyList<string> Errors,
    string SchemaVersion, string? WrittenTo);

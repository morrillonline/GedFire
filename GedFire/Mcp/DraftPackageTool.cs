using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GedCore.Drafting;
using GedCore.Policy;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The draft_package MCP tool: presents finished changesets for review. It
// compiles nothing. Each changeset file (made by draft_changeset, then
// validated and corrected upstream) is checked once more against the bound
// document, and a changeset that does not pass fails the call with its errors
// named. When all pass, one proposal markdown file is written presenting the
// evidence behind each changeset, numbered in the order the paths were given.
// Never touches the GEDCOM file.
// ---------------------------------------------------------------------------

public sealed class DraftPackageTool
{
    public const string ToolName = "draft_package";

    public const string Description =
        "Draft the proposal document that presents finished changesets for review. Takes changesetPaths, the " +
        "changeset files draft_changeset produced and that have already been validated (validate_changeset, " +
        "check_plausibility) and corrected, in the order they should be numbered, plus the proposal name and an " +
        "existing folder. Writes one markdown file, <folder>/<proposal>.md, the document the reviewer reads: for " +
        "each changeset, numbered so the reviewer can approve or deny it on its own, the findings each operation " +
        "cites (resolved through the finding references the changeset carries), their verbatim extracts, source " +
        "and information classes, and quality grades. It compiles nothing: a changeset that does not validate " +
        "against this server's document, or that carries no findings, fails the call with its errors named, and " +
        "nothing is written; fix the findings and draft the changeset again. The result reports the proposal's " +
        "path and each changeset it presents, with the sha256 of the exact bytes presented. An existing proposal " +
        "file is replaced only when overwrite is true. Reads the document, writes nothing to it.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "changesetPaths": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Paths to the finished changesets, each written by draft_changeset and already validated. Their order sets the changeset numbers."
            },
            "folder": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "An existing folder to write the proposal into."
            },
            "proposal": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "The proposal's name, the file name written: <proposal>.md."
            },
            "overwrite": {
              "type": "boolean",
              "description": "Replace the proposal file if it already exists. Default false."
            }
          },
          "required": ["changesetPaths", "folder", "proposal"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean", "description": "true only when every changeset validated and the proposal was written." },
            "proposalPath": { "type": ["string", "null"], "description": "The proposal file written, or null." },
            "proposalSha256": { "type": ["string", "null"], "description": "Lowercase hex sha256 of the proposal's bytes." },
            "changesets": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "number": { "type": "integer", "description": "The changeset's number in the proposal, by its position in changesetPaths." },
                  "path": { "type": "string" },
                  "sha256": { "type": ["string", "null"], "description": "Lowercase hex sha256 of the changeset file's bytes; null when it could not be read." },
                  "success": { "type": "boolean" },
                  "errors": { "type": "array", "items": { "type": "string" }, "description": "Why the changeset cannot be presented, each naming the file." }
                },
                "required": ["number", "path", "sha256", "success", "errors"]
              }
            },
            "errors": { "type": "array", "items": { "type": "string" }, "description": "Problems not tied to one changeset's validation, such as a changeset that carries no findings, or the proposal file that could not be written." }
          },
          "required": ["success", "proposalPath", "proposalSha256", "changesets", "errors"]
        }
        """;

    readonly ChangesetValidator _validator;
    readonly ToolGate _gate;

    public DraftPackageTool(string absoluteGedcomPath, ToolGate gate, ChangesetPolicy? policy = null)
    {
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _validator = new ChangesetValidator(absoluteGedcomPath, policy);
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.CreatesFiles)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by field name rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? changesetPaths = null, JsonElement? folder = null, JsonElement? proposal = null,
        JsonElement? overwrite = null, CancellationToken cancellationToken = default)
        => HandleAsync(
            new DraftPackageArguments(changesetPaths ?? default, folder ?? default, proposal ?? default, overwrite ?? default),
            cancellationToken);

    public Task<CallToolResult> HandleAsync(DraftPackageArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => Task.FromResult(Execute(arguments, ct)), cancellationToken);

    CallToolResult Execute(DraftPackageArguments arguments, CancellationToken cancellationToken)
    {
        if (!TryReadRequest(arguments, out var request, out string? error))
            return CallToolResults.Error(error!);

        var ready = new List<ProposalChangeset>();
        var changesets = new List<PackageChangesetResult>();
        foreach (var (path, index) in request!.ChangesetPaths.Select((p, i) => (p, i + 1)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            changesets.Add(Check(index, path, ready, cancellationToken));
        }

        var errors = new List<string>();
        string? proposalPath = null;
        string? proposalSha = null;
        if (changesets.All(c => c.Success))
        {
            var document = ProposalWriter.Write(request.Proposal, ready);
            errors.AddRange(document.Errors);
            if (document.Success) (proposalPath, proposalSha) = WriteProposal(request, document.Markdown!, errors);
        }

        return CallToolResults.Success(
            new DraftPackageResult(proposalPath is not null && errors.Count == 0, proposalPath, proposalSha, changesets, errors),
            CallToolResults.JsonOptions);
    }

    PackageChangesetResult Check(int number, string path, List<ProposalChangeset> ready, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new PackageChangesetResult(number, path, null, false, [$"{path} could not be read: {ex.Message}"]);
        }

        string sha = Sha256Hex(bytes);
        using var reader = new StreamReader(new MemoryStream(bytes));
        string json = reader.ReadToEnd();
        var errors = _validator.Check(path, json, cancellationToken);
        if (errors.Count == 0) ready.Add(new ProposalChangeset(path, sha, json));
        return new PackageChangesetResult(number, path, sha, errors.Count == 0, errors);
    }

    static (string? Path, string? Sha256) WriteProposal(Request request, string markdown, List<string> errors)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(markdown);
        string path = Path.Combine(request.Folder, $"{request.Proposal}.md");
        if (ChangesetFileWriter.TryWrite(path, bytes, request.Overwrite, out string? writeError) is null)
        {
            errors.Add(writeError!);
            return (null, null);
        }
        return (path, Sha256Hex(bytes));
    }

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    sealed record Request(IReadOnlyList<string> ChangesetPaths, string Folder, string Proposal, bool Overwrite);

    static bool TryReadRequest(DraftPackageArguments arguments, out Request? request, out string? error)
    {
        request = null;
        if (arguments.ChangesetPaths.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            error = "changesetPaths is required: an array of changeset file paths.";
            return false;
        }
        if (!ToolArguments.TryReadOptionalStringList(arguments.ChangesetPaths, "changesetPaths", out var names, out error)) return false;
        if (names.Count == 0)
        {
            error = "changesetPaths must name at least one changeset file.";
            return false;
        }

        var paths = names.Select(Path.GetFullPath).ToList();
        if (paths.GroupBy(p => p, PathComparer).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            error = $"changesetPaths names {twice.Key} more than once.";
            return false;
        }

        if (!TryReadRequiredText(arguments.Folder, "folder", out string? folder, out error)) return false;
        if (!TryReadRequiredText(arguments.Proposal, "proposal", out string? proposal, out error)) return false;
        if (proposal!.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = $"proposal \"{proposal}\" cannot be used in a file name.";
            return false;
        }

        string fullFolder = Path.GetFullPath(folder!);
        if (!Directory.Exists(fullFolder))
        {
            error = $"folder {fullFolder} does not exist.";
            return false;
        }

        if (!ToolArguments.TryReadOptionalBool(arguments.Overwrite, "overwrite", out bool overwrite, out error)) return false;

        request = new Request(paths, fullFolder, proposal, overwrite);
        return true;
    }

    static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    static bool TryReadRequiredText(JsonElement element, string field, out string? value, out string? error)
    {
        if (!ToolArguments.TryReadOptionalString(element, field, out value, out error)) return false;
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            error = $"{field} is required.";
            return false;
        }
        return true;
    }
}

public sealed record DraftPackageArguments(JsonElement ChangesetPaths, JsonElement Folder, JsonElement Proposal, JsonElement Overwrite);

/// <summary>One changeset the proposal presents, or fails to: its number, path, the hash of its bytes, and why it cannot be presented.</summary>
public sealed record PackageChangesetResult(int Number, string Path, string? Sha256, bool Success, IReadOnlyList<string> Errors);

public sealed record DraftPackageResult(
    bool Success, string? ProposalPath, string? ProposalSha256, IReadOnlyList<PackageChangesetResult> Changesets, IReadOnlyList<string> Errors);

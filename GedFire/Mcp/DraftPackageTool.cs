using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Drafting;
using GedCore.Points;
using GedCore.Policy;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The draft_package MCP tool: drafts a whole proposal package from the lake's
// correlated finding files in one call. The files are grouped by the anchor
// each was correlated to; each anchor's findings are compiled into one
// changeset, dry-run against the bound document (points preview and
// plausibility come from that one run), and written to
// <folder>/<proposal>.item<N>.changes.json only when everything passes. An
// anchor that fails never stops the others. Never touches the GEDCOM file.
// ---------------------------------------------------------------------------

public sealed class DraftPackageTool
{
    public const string ToolName = "draft_package";

    public const string Description =
        "Draft a whole proposal package from the lake's correlated finding files in one call. The files in " +
        "findingPaths are grouped by the anchor each was correlated to (correlation.target); each anchor's " +
        "findings are compiled exactly as draft_changeset compiles them (findingPaths), dry-run against this " +
        "server's document as validate_changeset and check_plausibility would, and written to " +
        "<folder>/<proposal>.item<N>.changes.json only when the draft, the policy and the dry run all pass. " +
        "Items are numbered by the first appearance of each anchor in findingPaths. An anchor that fails does " +
        "not stop the others: every item reports its files, finding ids, errors, decisions, pointsPreview " +
        "(priced from the cards under its anchor in cardsPath, a JSON file mapping each anchor xref to the " +
        "cards array validate_changeset takes), the plausibility findings the changeset would introduce, and " +
        "for a written file its path and sha256. A finding file with no draft, a parked finding, or a " +
        "non-empty draftGaps fails its anchor by file name. An existing file is replaced only when overwrite " +
        "is true. Reads the document, writes nothing to it.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "findingPaths": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Paths to the package's correlated finding files. Their order sets the item numbers."
            },
            "folder": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "An existing folder to write the changeset files into."
            },
            "proposal": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "The package's name, the prefix of every file written: <proposal>.item<N>.changes.json."
            },
            "cardsPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "A JSON file mapping each anchor xref to the array of research cards its item serves, each in validate_changeset's cards shape. An anchor with no entry gets no pointsPreview."
            },
            "overwrite": {
              "type": "boolean",
              "description": "Replace changeset files that already exist. Default false."
            }
          },
          "required": ["findingPaths", "folder", "proposal"]
        }
        """;

    public static readonly string OutputSchemaJson = BuildOutputSchema();

    static string BuildOutputSchema()
    {
        var preview = JsonNode.Parse(ChangesetToolSchemas.PointsPreviewJson)!;
        var schema = JsonNode.Parse("""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "success": { "type": "boolean", "description": "true only when every anchor was written and no finding file was left unplaced." },
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "item": { "type": "integer", "description": "The item number, by first appearance of the anchor." },
                      "anchor": { "type": "string" },
                      "files": { "type": "array", "items": { "type": "string" }, "description": "The finding files of this anchor, in the order given." },
                      "findingIds": { "type": "array", "items": { "type": "string" } },
                      "success": { "type": "boolean" },
                      "writtenTo": { "type": ["string", "null"] },
                      "sha256": { "type": ["string", "null"], "description": "Lowercase hex sha256 of the bytes written." },
                      "errors": { "type": "array", "items": { "type": "string" } },
                      "decisions": { "type": "array", "items": { "type": "object" }, "description": "The same decisions draft_changeset returns." },
                      "pointsPreview": {},
                      "plausibility": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "code": { "type": "string" },
                            "severity": { "type": "string" },
                            "message": { "type": "string" },
                            "xref": { "type": ["string", "null"] }
                          },
                          "required": ["code", "severity", "message", "xref"]
                        }
                      }
                    },
                    "required": ["item", "anchor", "files", "findingIds", "success", "writtenTo", "sha256", "errors", "decisions", "pointsPreview", "plausibility"]
                  }
                },
                "errors": { "type": "array", "items": { "type": "string" }, "description": "Problems not tied to one anchor, such as a finding file whose anchor could not be read." }
              },
              "required": ["success", "items", "errors"]
            }
            """)!;
        schema["properties"]!["items"]!["items"]!["properties"]!["pointsPreview"] =
            new JsonObject { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "null" }, preview) };
        return schema.ToJsonString();
    }

    readonly DocumentSession _session;
    readonly string _absoluteGedcomPath;
    readonly ToolGate _gate;
    readonly ChangesetPolicy? _policy;

    public DraftPackageTool(DocumentSession session, string absoluteGedcomPath, ToolGate gate, ChangesetPolicy? policy = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _absoluteGedcomPath = absoluteGedcomPath;
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _policy = policy;
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.CreatesFiles)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by field name rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? findingPaths = null, JsonElement? folder = null, JsonElement? proposal = null,
        JsonElement? cardsPath = null, JsonElement? overwrite = null, CancellationToken cancellationToken = default)
        => HandleAsync(
            new DraftPackageArguments(findingPaths ?? default, folder ?? default, proposal ?? default, cardsPath ?? default, overwrite ?? default),
            cancellationToken);

    public Task<CallToolResult> HandleAsync(DraftPackageArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(arguments, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(DraftPackageArguments arguments, CancellationToken cancellationToken)
    {
        if (!TryReadRequest(arguments, out var request, out string? error))
            return CallToolResults.Error(error!);

        var plan = PackagePlanner.Plan(FindingFileLoader.LoadAll(request!.FindingPaths));
        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var itemDrafter = new PackageItemDrafter(
            new ChangesetDrafter(new ModelDraftLookup(snapshot.Model), _policy), _absoluteGedcomPath, request.Cards);

        var items = new List<PackageItemResult>();
        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(request.Folder, $"{request.Proposal}.item{item.Number}.changes.json");
            items.Add(itemDrafter.Draft(item, path, request.Overwrite, cancellationToken));
        }

        var errors = plan.Unassigned.SelectMany(f => f.Errors.Select(e => $"{e} (no anchor could be read from it, so it belongs to no item)")).ToList();
        return CallToolResults.Success(
            new DraftPackageResult(items.All(i => i.Success) && errors.Count == 0, items, errors), CallToolResults.JsonOptions);
    }

    sealed record Request(IReadOnlyList<string> FindingPaths, string Folder, string Proposal, bool Overwrite, IReadOnlyDictionary<string, IReadOnlyList<PointsCard>> Cards);

    static bool TryReadRequest(DraftPackageArguments arguments, out Request? request, out string? error)
    {
        request = null;
        if (arguments.FindingPaths.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            error = "findingPaths is required: an array of finding file paths.";
            return false;
        }
        if (!ToolArguments.TryReadOptionalStringList(arguments.FindingPaths, "findingPaths", out var paths, out error)) return false;
        if (paths.Count == 0)
        {
            error = "findingPaths must name at least one finding file.";
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
        if (!TryReadCards(arguments.CardsPath, out var cards, out error)) return false;

        request = new Request(paths, fullFolder, proposal, overwrite, cards);
        return true;
    }

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

    static bool TryReadCards(JsonElement cardsPath, out IReadOnlyDictionary<string, IReadOnlyList<PointsCard>> cards, out string? error)
    {
        cards = new Dictionary<string, IReadOnlyList<PointsCard>>();
        if (!ToolArguments.TryReadOptionalString(cardsPath, "cardsPath", out string? path, out error)) return false;
        if (path is null) return true;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "cardsPath must not be blank.";
            return false;
        }

        string fullPath = Path.GetFullPath(path.Trim());
        try
        {
            using var stream = File.OpenRead(fullPath);
            return PackageCardsReader.TryRead(stream, $"cardsPath {fullPath}", out cards, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"cardsPath {fullPath} could not be read: {ex.Message}";
            return false;
        }
    }
}

public sealed record DraftPackageArguments(
    JsonElement FindingPaths, JsonElement Folder, JsonElement Proposal, JsonElement CardsPath, JsonElement Overwrite);

public sealed record DraftPackageResult(bool Success, IReadOnlyList<PackageItemResult> Items, IReadOnlyList<string> Errors);

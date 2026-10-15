using GedCore;
using GedCore.Validate;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The research_brief MCP tool: one person's whole research dispatch packet in
// a single call — the full record, one-line kin, the gaps select_targets
// would score, duplicate candidates, standing plausibility findings, and all
// note texts — replacing the get_record + get_records(kin) + find_person fan
// every research task starts with. Reads the document only.
// ---------------------------------------------------------------------------

public sealed class ResearchBriefTool
{
    public const string ToolName = "research_brief";

    static readonly string[] AcceptedCardTypes =
        ["new-parent", "new-spouse", "new-child", "enrich-person", "enrich-birth", "enrich-death", "enrich-marriage"];

    public const string Description =
        "One call for everything research on a person starts with, instead of a get_record plus a " +
        "get_records over every relative plus a find_person duplicate pass. Returns, for the given xref: " +
        "person (the full record detail get_record returns, facts with dates, places, citations, and notes); " +
        "kin (parents, siblings, spouses with marriage date and place, and children, each as a one-line " +
        "{xref, name, born, died} summary, not a full record); gaps (exactly the targets select_targets " +
        "would report for this person, in the same record shape, so the caller sees what would score); " +
        "possibleDuplicateOf and possibleDuplicateCount (the same-person candidates select_targets computes, " +
        "best first); plausibility (the findings check_plausibility's rules raise about this person or their " +
        "families as the document stands — age anomalies, event-order problems); and notes (the person's " +
        "NOTE texts plus NOTE texts on their families, where research remarks often live). An optional " +
        "cardType — new-parent, new-spouse, new-child, enrich-person, enrich-birth, enrich-death, or " +
        "enrich-marriage — adds an emphasis section: for new-spouse, the children whose other parent is " +
        "unrecorded; for enrich-birth/enrich-death/enrich-person, the dated facts that bound the missing " +
        "vital. A person with no kin, no notes, and no duplicates returns empty arrays, not errors. External " +
        "research files and prior search attempts are out of scope: the caller merges those itself. Reads " +
        "the document only; writes nothing.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "xref": {
              "type": "string",
              "minLength": 1,
              "description": "The person's xref, e.g. \"@I123@\". Must be a person in this document."
            },
            "cardType": {
              "type": "string",
              "enum": ["new-parent", "new-spouse", "new-child", "enrich-person", "enrich-birth", "enrich-death", "enrich-marriage"],
              "description": "Optional research-card type to emphasize (case and spacing ignored)."
            }
          },
          "required": ["xref"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "xref": { "type": "string" },
            "cardType": { "type": ["string", "null"], "description": "The normalized cardType echoed back, or null." },
            "person": { "type": "object", "description": "The full person record, in the exact shape get_record returns." },
            "kin": {
              "type": "object",
              "properties": {
                "parents": { "type": "array", "items": { "$ref": "#/$defs/kinSummary" } },
                "siblings": { "type": "array", "items": { "$ref": "#/$defs/kinSummary" } },
                "spouses": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "familyXref": { "type": "string" },
                      "spouse": { "oneOf": [{ "$ref": "#/$defs/kinSummary" }, { "type": "null" }] },
                      "marriageDate": { "type": ["string", "null"] },
                      "marriagePlace": { "type": ["string", "null"] }
                    },
                    "required": ["familyXref", "spouse", "marriageDate", "marriagePlace"]
                  }
                },
                "children": { "type": "array", "items": { "$ref": "#/$defs/kinSummary" } }
              },
              "required": ["parents", "siblings", "spouses", "children"]
            },
            "gaps": {
              "type": "array",
              "items": { "type": "object" },
              "description": "Exactly the targets select_targets would report for this person, in the same record shape (duplicates are not annotated here; see possibleDuplicateOf)."
            },
            "possibleDuplicateOf": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": { "xref": { "type": "string" }, "score": { "type": "number" } },
                "required": ["xref", "score"]
              },
              "description": "Up to three best GEN301 duplicate candidates, best first, as select_targets computes them."
            },
            "possibleDuplicateCount": { "type": "integer" },
            "plausibility": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "code": { "type": "string" },
                  "severity": { "type": "string" },
                  "message": { "type": "string" },
                  "xref": { "type": ["string", "null"] }
                },
                "required": ["code", "severity", "message", "xref"]
              },
              "description": "Findings anchored on this person or their families, as the document stands."
            },
            "notes": {
              "type": "object",
              "properties": {
                "person": { "type": "array", "items": { "type": "string" } },
                "families": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "xref": { "type": "string" },
                      "texts": { "type": "array", "items": { "type": "string" } }
                    },
                    "required": ["xref", "texts"]
                  }
                }
              },
              "required": ["person", "families"]
            },
            "emphasis": {
              "type": ["object", "null"],
              "properties": {
                "childrenWithUnknownOtherParent": { "type": ["array", "null"], "items": { "$ref": "#/$defs/kinSummary" } },
                "birthBoundingFacts": { "type": ["array", "null"], "items": { "$ref": "#/$defs/briefFact" } },
                "deathBoundingFacts": { "type": ["array", "null"], "items": { "$ref": "#/$defs/briefFact" } }
              },
              "description": "Present only when cardType was given."
            }
          },
          "required": ["xref", "cardType", "person", "kin", "gaps", "possibleDuplicateOf", "possibleDuplicateCount", "plausibility", "notes"],
          "$defs": {
            "kinSummary": {
              "type": "object",
              "properties": {
                "xref": { "type": "string" },
                "name": { "type": "string" },
                "born": { "type": ["string", "null"] },
                "died": { "type": ["string", "null"] }
              },
              "required": ["xref", "name", "born", "died"]
            },
            "briefFact": {
              "type": "object",
              "properties": {
                "tag": { "type": "string" },
                "date": { "type": ["string", "null"] },
                "place": { "type": ["string", "null"] }
              },
              "required": ["tag", "date", "place"]
            }
          }
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;
    readonly string _absoluteGedcomPath;
    readonly ResearchBriefBuilder _builder;

    public ResearchBriefTool(DocumentSession session, ToolGate gate, string absoluteGedcomPath, string mediaDirectory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        if (string.IsNullOrEmpty(absoluteGedcomPath)) throw new ArgumentException("Path must not be empty.", nameof(absoluteGedcomPath));
        _absoluteGedcomPath = absoluteGedcomPath;
        _builder = new ResearchBriefBuilder(mediaDirectory);
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent)
            .CreateTool(InvokeAsync);

    Task<CallToolResult> InvokeAsync(string xref, string? cardType = null, CancellationToken cancellationToken = default)
        => HandleAsync(xref, cardType, cancellationToken);

    public Task<CallToolResult> HandleAsync(string xref, string? cardType, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(xref, cardType, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(string xref, string? cardType, CancellationToken cancellationToken)
    {
        string trimmed = xref?.Trim() ?? "";
        if (trimmed.Length == 0)
            return CallToolResults.Error("xref must be a non-blank person xref, e.g. \"@I123@\".");

        if (!TryNormalizeCardType(cardType, out string? normalizedCardType, out string? error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Model.Individuals.TryGetValue(trimmed, out var person))
            return CallToolResults.Error($"xref: {trimmed} is not a person in this document.");

        IReadOnlyList<GedDiagnostic> findings;
        try
        {
            // The checker works on the parsed document, not the model; scoping
            // the duplicate rule to this xref keeps the sweep cheap (and its
            // pairs are discarded anyway in favor of possibleDuplicateOf).
            var doc = GedReader.ReadFile(_absoluteGedcomPath);
            findings = PlausibilityChecker.Check(
                doc, new HashSet<string>([trimmed], StringComparer.Ordinal), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CallToolResults.Error($"Could not open '{_absoluteGedcomPath}': {ex.Message}");
        }

        var brief = _builder.Build(snapshot, person, normalizedCardType, findings, cancellationToken);
        return CallToolResults.Success(brief, CallToolResults.JsonOptions);
    }

    // Case and spacing are ignored, matching how points cards read the same vocabulary.
    static bool TryNormalizeCardType(string? cardType, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(cardType)) return true;

        string candidate = cardType.Trim().ToLowerInvariant().Replace(' ', '-');
        if (AcceptedCardTypes.Contains(candidate))
        {
            normalized = candidate;
            return true;
        }
        error = $"cardType must be one of {string.Join(", ", AcceptedCardTypes)}, got \"{cardType}\".";
        return false;
    }
}

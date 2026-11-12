using System.Text.Json;
using GedFire.Gen;
using GedFire.TargetSelection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The select_targets MCP tool: the `gedfire select-targets` command as a tool.
// Same gap detection, same draw, same document as the command's wanted.json,
// returned as the result instead of written to a file and without the GEDCOM
// path. The draw is seeded from the clock exactly as the command's is. With
// explicit xrefs there is no draw at all: the given people are scored
// deterministically and draw.seed is null.
// ---------------------------------------------------------------------------

public sealed class SelectTargetsTool
{
    public const string ToolName = "select_targets";
    public const int MaxXrefs = 500;

    public const string Description =
        "Draw research targets from this server's GEDCOM: people with a missing fact or relationship (a " +
        "missing parent, spouse, or child, or a date or place that is not exact) among the people who bear, or " +
        "are married to someone who bears, one of the given surnames. Draws \"people\" people at random from " +
        "those with at least one gap and returns every card that pays for each of them, kept together: one " +
        "target per eligible gap (a married person gets no New spouse card, a person with both parents no New " +
        "parent card), then, for a man, his Biography card. Each target has its gap type, difficulty band, points, and the facts needed to " +
        "start work (an Enrich person target is one card per person: \"enrichments\" lists every birth, " +
        "marriage and death with an inexact date or place, or an exact one lacking a citation, nominalPoints is one per missing date, place or citation, and " +
        "the difficulty bonus is added once; a BET range of 12 months or less cited at QUAY 2 or higher is not a missing date), including possibleDuplicateOf (the best few people the duplicate test pairs with the target) and possibleDuplicateCount; the " +
        "draw keeps at most one person with Legendary-band cards (that person may hold several), discarding later such people whole, so a pack can come up short. " +
        "A family's New child card and marriage enrichment sit on one spouse only. This is " +
        "the same draw as the gedfire select-targets command. People born within the last 100 years are " +
        "never selected. Pass xrefs instead of people and surnames to skip the draw and score exactly those " +
        "people the same way: the result is " +
        "deterministic, draw.seed is null, and xrefsWithoutGaps names the people that produced no gap target. " +
        "A Biography target is a repeatable card for a man's cited biographical prose (service, occupation, migration, " +
        "probate...), offered only beside another of his cards, and open even when prose exists; nominalPoints 3 plus the enrichment difficulty bonus (birth era and region), paid " +
        "once per person per changeset when a biography note gains a citation (source and page) none of the person's " +
        "cited notes carried. " +
        "New parent and New spouse require an exact date or non-empty place on an own vital event " +
        "(birth, christening/baptism, death, burial). New child requires an anchor on either spouse or " +
        "the marriage. Otherwise enrich first; suppressedCards lists the withheld cards and reasons.";

    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "people": {
              "type": "integer",
              "minimum": 1,
              "description": "How many people to draw; each brings every card that pays for them. Required unless xrefs is given."
            },
            "surnames": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "Surnames to draw targets for, e.g. [\"Ashworth\", \"Ashwirth\"]. Required unless xrefs is given."
            },
            "xrefs": {
              "type": "array",
              "minItems": 1,
              "maxItems": 500,
              "items": { "type": "string", "pattern": "^@[^@]+@$", "minLength": 3 },
              "description": "Person xrefs to score instead of drawing, e.g. [\"@I00006@\"]. Every eligible gap becomes a target; suppressedCards reports relationship gaps needing enrichment first.               Cannot be combined with people or surnames."
            }
          },
          "oneOf": [
                          { "required": ["people", "surnames"] },
            { "required": ["xrefs"] }
          ]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "generated": { "type": "string", "description": "UTC date of the draw, yyyy-MM-dd." },
            "surnames": { "type": "array", "items": { "type": "string" }, "description": "Empty when xrefs was given." },
            "totalCandidates": { "type": "integer", "minimum": 0, "description": "Eligible gap cards before the draw, excluding suppressedCards and Biography cards." },
            "count": { "type": "integer", "minimum": 0, "description": "Cards returned in targets, across every drawn person; the people drawn can be fewer than requested." },
            "targets": { "type": "array", "items": { "type": "object" } },
            "suppressedCards": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "xref": { "type": "string" },
                  "name": { "type": "string" },
                  "cardType": { "type": "string" },
                  "reason": { "type": "string" }
                },
                "required": ["xref", "name", "cardType", "reason"]
              },
              "description": "Relationship cards withheld until the subject or family has an anchor; enrich first."
            },
            "xrefsWithoutGaps": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Only with xrefs: the input xrefs that produced no gap target (a Biography target is not a gap)."
            },
            "draw": {
              "type": "object",
              "properties": {
                "seed": { "type": ["integer", "null"], "description": "Null when xrefs was given: nothing was drawn." },
                "legendaryDiscards": { "type": "array", "items": { "type": "object" } }
              },
              "required": ["seed", "legendaryDiscards"]
            }
          },
          "required": ["generated", "surnames", "totalCandidates", "count", "targets", "suppressedCards", "draw"]
        }
        """;

    readonly DocumentSession _session;
    readonly ToolGate _gate;

    public SelectTargetsTool(DocumentSession session, ToolGate gate)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public McpServerTool ToMcpServerTool() =>
        new ToolDefinition(ToolName, Description, InputSchemaJson, OutputSchemaJson, ToolBehavior.ReadOnlyNonIdempotent)
            .CreateTool(InvokeAsync);

    // Arguments arrive as raw JsonElements so a wrong shape is reported by
    // field name in ExecuteAsync rather than failing inside the SDK binder.
    Task<CallToolResult> InvokeAsync(
        JsonElement? people = null, JsonElement? surnames = null, JsonElement? xrefs = null,
        CancellationToken cancellationToken = default)
        => HandleAsync(people ?? default, surnames ?? default, xrefs ?? default, cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement people, JsonElement surnames, JsonElement xrefs, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(people, surnames, xrefs, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(JsonElement people, JsonElement surnames, JsonElement xrefs, CancellationToken cancellationToken)
    {
        bool hasXrefs = xrefs.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (hasXrefs) return await ExecuteForXrefsAsync(people, surnames, xrefs, cancellationToken).ConfigureAwait(false);

        if (people.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return CallToolResults.Error("people is required: a positive integer, the number of people to draw.");
        if (!ToolArguments.TryReadOptionalInt(people, "people", 1, int.MaxValue, 1, out int peopleCount, out string? error))
            return CallToolResults.Error(error!);
        if (!ToolArguments.TryReadOptionalStringList(surnames, "surnames", out var surnameList, out error))
            return CallToolResults.Error(error!);
        if (surnameList.Count == 0)
            return CallToolResults.Error("surnames is required: an array with at least one surname such as [\"Ashworth\"].");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var suppressedCards = new List<SuppressedCard>();
        var candidates = GapDetector.Detect(snapshot.Model, surnameList, suppressedCards);
        var draw = DuplicateAnnotator.Annotate(
          TargetDrawer.DrawPack(snapshot.Model, candidates, peopleCount, DateTime.UtcNow.Ticks) with { SuppressedCards = suppressedCards },
          snapshot.MatchIndex, cancellationToken);

        string json = WantedFileWriter.ToJson(surnameList, candidates.Count, draw);
        return Success(json);
    }

    // No draw: score exactly the given people, so the result is deterministic.
    async Task<CallToolResult> ExecuteForXrefsAsync(JsonElement peopleToDraw, JsonElement surnames, JsonElement xrefs, CancellationToken cancellationToken)
    {
        if (peopleToDraw.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ||
            surnames.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            return CallToolResults.Error("xrefs cannot be combined with people or surnames: it scores exactly the given people, with no draw.");
        if (!ToolArguments.TryReadXrefList(xrefs, "xrefs", MaxXrefs, out var list, out string? error))
            return CallToolResults.Error(error!);

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var people = new List<GedIndividual>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string xref in list)
        {
            if (!snapshot.Model.Individuals.TryGetValue(xref, out var person))
                return CallToolResults.Error($"xrefs: {xref} is not a person in this document.");
            if (seen.Add(xref)) people.Add(person);
        }

        var suppressedCards = new List<SuppressedCard>();
        var gaps = GapDetector.DetectForPeople(people, suppressedCards);
        var scored = DuplicateAnnotator.Annotate(
          new DrawResult { Targets = GapDetector.WithBiographyCards(people, gaps), Seed = null, LegendaryDiscards = [], SuppressedCards = suppressedCards },
            snapshot.MatchIndex, cancellationToken);
        var produced = gaps.Select(t => t.Xref).ToHashSet(StringComparer.Ordinal);
        var withoutGaps = people.Select(p => p.Xref).Where(x => !produced.Contains(x)).ToList();

        return Success(WantedFileWriter.ToJsonForXrefs(withoutGaps, scored));
    }

    static CallToolResult Success(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CallToolResults.Success(document.RootElement.Clone(), CallToolResults.JsonOptions);
    }
}

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
        "those with at least one gap (a person whose withheld cards wait only on an anchor enrichment would write is " +
        "weighted by one more for each such card, since enriching them also unlocks those cards) and returns every card that pays for each of them, kept together: one " +
        "target per eligible gap (a married person gets no New spouse card, a person with both parents no New " +
        "parent card), then, for a man, his Biography card. Each target has its gap type, difficulty band, points, and the facts needed to " +
        "start work (an Enrich person target is one card per person: \"enrichments\" lists every birth, " +
        "marriage and death whose date or place is not exact or that has no citation, nominalPoints is one per missing date, place or citation (a fact with no citation misses one whatever its date and place), and " +
        "the difficulty bonus is added once; a BET range of 12 months or less cited at QUAY 2 or higher is not a missing date; validate_changeset's pointsPreview pays the card by this same rule, one point for each listed part a changeset fills), including possibleDuplicateOf (the best few people the duplicate test pairs with the target) and possibleDuplicateCount; the " +
        "draw keeps at most one person with Legendary-band cards (that person may hold several); a later such person is passed over whole and the next person drawn takes the seat. " +
        "It also seats at most two people whose only gap card is Enrich person while people with a New parent, New spouse or New child card remain, " +
        "setting the rest aside to fill only seats no one else can, so a pack comes up short only when the pool runs out. " +
        "A family's New child card and marriage enrichment sit on one spouse only. This is " +
        "the same draw as the gedfire select-targets command. People born within the last 100 years are " +
        "never drawn. Pass xrefs instead of people and surnames to skip the draw and score exactly those " +
        "people the same way, whenever they were born: the result is " +
        "deterministic, draw.seed is null, and xrefsWithoutGaps names the people that got no card at all (such as a woman with no gap). " +
        "A Biography target is a repeatable card for a man's biography beyond birth, death and marriage (service, occupation, " +
        "census, residence, migration, probate, a divorce...): in the draw it is offered only beside another of his cards, while a man named in xrefs gets it even with no other gap; " +
        "it is dealt only to men, but validate_changeset pays a Biography card for a woman the same way; " +
        "it is open even when prose exists; nominalPoints 3 plus the enrichment difficulty bonus (birth era and region), paid " +
        "once per person per changeset that adds new information: a cited fact other than birth, death or marriage added, improved " +
        "or corrected on him or his family; or, in a biography note, a citation (source and page) none of the person's " +
        "cited notes carried, or text that matches none of the person's notes once case, whitespace and punctuation are folded. " +
        "New parent and New spouse require an exact date or non-empty place on an own vital event " +
        "(birth, christening/baptism, death, burial). New spouse is also withheld from a person with a cited NMR 0, a proven never-married answer. New child requires an anchor on either spouse or " +
        "the marriage. Otherwise enrich first. A childless couple's New child card is also withheld when, on every reading of " +
        "the dates (an ABT, CAL or EST date reads as two years either side), the wife was at least maxMotherAgeAtMarriage " +
        "(default 50) at the marriage, or the marriage lasted under minMarriageYears (default 1) before either spouse's death " +
        "or burial or a divorce; an unknown wife's birth or marriage date, or a child of either spouse born after the marriage, " +
        "never withholds it. suppressedCards lists the withheld cards and reasons; with xrefs, includeSuppressed: true draws them anyway. " +
        "Pass studySurnames, in either mode, to hold the result to a surname study's scope, on the assumption that a child takes the father's surname: " +
        "a person who bears one of them gets every card; a spouse of such a person who does not bear one gets only Enrich person; anyone else gets none; " +
        "and a family's New child card is dealt, to either spouse, only when its husband bears one. Each card it withholds is listed in suppressedCards with a " +
        "reason starting \"out of study scope:\" (in a draw, for the people drawn only), and includeSuppressed does not force such a card.";

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
              "description": "Person xrefs to score instead of drawing, e.g. [\"@I00006@\"]. Every eligible gap becomes a target, and a named man also gets his Biography card even with no other gap; suppressedCards reports the cards withheld and why. Cannot be combined with people or surnames."
            },
            "maxMotherAgeAtMarriage": {
              "type": "integer",
              "minimum": 1,
              "maximum": 150,
              "default": 50,
              "description": "Withhold a childless couple's New child card when the wife's youngest possible age at the marriage is at least this. Default 50."
            },
            "minMarriageYears": {
              "type": "integer",
              "minimum": 0,
              "maximum": 100,
              "default": 1,
              "description": "Withhold a childless couple's New child card when the marriage's longest possible length, to either spouse's death or burial or a divorce, is under this many years. Default 1; 0 turns the rule off."
            },
            "includeSuppressed": {
              "type": "boolean",
              "default": false,
              "description": "Only with xrefs: draw every card a rule would withhold, so the owner can force one. suppressedCards then lists only the cards studySurnames withholds, which are never forced."
            },
            "studySurnames": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "string", "minLength": 1, "pattern": "\\S" },
              "description": "The surnames of a surname study, e.g. [\"Ashworth\", \"Ashwirth\"]. With people or with xrefs, cards outside the study's scope are withheld and listed in suppressedCards: every card of a person who neither bears one of these nor is married to someone who does; New parent, New spouse and Biography of a spouse who does not bear one; and New child of a family whose husband does not bear one. Omit it to deal as before."
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
              "description": "Cards withheld, with the rule and the values behind it: relationship gaps needing enrichment first, a New spouse card answered by a cited never-married fact, New child cards the couple almost certainly could not win, and, with studySurnames, cards outside the study's scope."
            },
            "xrefsWithoutGaps": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Only with xrefs: the input xrefs that got no card at all. A named man always gets at least his Biography card."
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
        JsonElement? maxMotherAgeAtMarriage = null, JsonElement? minMarriageYears = null, JsonElement? includeSuppressed = null,
        JsonElement? studySurnames = null,
        CancellationToken cancellationToken = default)
        => HandleAsync(new SelectTargetsArguments
        {
            People = people ?? default,
            Surnames = surnames ?? default,
            Xrefs = xrefs ?? default,
            MaxMotherAgeAtMarriage = maxMotherAgeAtMarriage ?? default,
            MinMarriageYears = minMarriageYears ?? default,
            IncludeSuppressed = includeSuppressed ?? default,
            StudySurnames = studySurnames ?? default,
        }, cancellationToken);

    public Task<CallToolResult> HandleAsync(
        JsonElement people, JsonElement surnames, JsonElement xrefs, CancellationToken cancellationToken) =>
        HandleAsync(new SelectTargetsArguments { People = people, Surnames = surnames, Xrefs = xrefs }, cancellationToken);

    public Task<CallToolResult> HandleAsync(SelectTargetsArguments arguments, CancellationToken cancellationToken) =>
        GatedToolRunner.RunAsync(_gate, ct => ExecuteAsync(arguments, ct), cancellationToken);

    async Task<CallToolResult> ExecuteAsync(SelectTargetsArguments arguments, CancellationToken cancellationToken)
    {
        bool hasXrefs = arguments.Xrefs.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (!TryReadRules(arguments, hasXrefs, out var rules, out string? error))
            return CallToolResults.Error(error!);
        if (hasXrefs) return await ExecuteForXrefsAsync(arguments, rules, cancellationToken).ConfigureAwait(false);

        var people = arguments.People;
        if (people.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return CallToolResults.Error("people is required: a positive integer, the number of people to draw.");
        if (!ToolArguments.TryReadOptionalInt(people, "people", 1, int.MaxValue, 1, out int peopleCount, out error))
            return CallToolResults.Error(error!);
        if (!ToolArguments.TryReadOptionalStringList(arguments.Surnames, "surnames", out var surnameList, out error))
            return CallToolResults.Error(error!);
        if (surnameList.Count == 0)
            return CallToolResults.Error("surnames is required: an array with at least one surname such as [\"Ashworth\"].");

        var snapshot = await _session.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var suppressedCards = new List<SuppressedCard>();
        var candidates = GapDetector.Detect(snapshot.Model, surnameList, suppressedCards, rules);
        var draw = DuplicateAnnotator.Annotate(
          TargetDrawer.DrawPack(snapshot.Model, candidates, peopleCount, DateTime.UtcNow.Ticks, suppressedCards, rules),
          snapshot.MatchIndex, cancellationToken);

        string json = WantedFileWriter.ToJson(surnameList, candidates.Count, draw);
        return Success(json);
    }

    static bool TryReadRules(SelectTargetsArguments arguments, bool hasXrefs, out GapRules rules, out string? error)
    {
        rules = GapRules.Default;
        var defaults = NewChildLimits.Default;
        if (!ToolArguments.TryReadOptionalInt(arguments.MaxMotherAgeAtMarriage, "maxMotherAgeAtMarriage", 1, 150,
                defaults.MaxMotherAgeAtMarriage, out int maxAge, out error) ||
            !ToolArguments.TryReadOptionalInt(arguments.MinMarriageYears, "minMarriageYears", 0, 100,
                defaults.MinMarriageYears, out int minYears, out error) ||
            !ToolArguments.TryReadOptionalBool(arguments.IncludeSuppressed, "includeSuppressed", out bool includeSuppressed, out error) ||
            !StudyScopeArgument.TryRead(arguments.StudySurnames, out var scope, out error))
            return false;
        if (includeSuppressed && !hasXrefs)
        {
            error = "includeSuppressed needs xrefs: only a person named explicitly can be forced past a rule.";
            return false;
        }

        rules = new GapRules { NewChild = new NewChildLimits(maxAge, minYears), IncludeSuppressed = includeSuppressed, Scope = scope };
        return true;
    }

    // No draw: score exactly the given people, so the result is deterministic.
    async Task<CallToolResult> ExecuteForXrefsAsync(SelectTargetsArguments arguments, GapRules rules, CancellationToken cancellationToken)
    {
        if (arguments.People.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ||
            arguments.Surnames.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            return CallToolResults.Error("xrefs cannot be combined with people or surnames: it scores exactly the given people, with no draw.");
        if (!ToolArguments.TryReadXrefList(arguments.Xrefs, "xrefs", MaxXrefs, out var list, out string? error))
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
        var gaps = GapDetector.DetectForPeople(people, suppressedCards, rules);
        var targets = GapDetector.WithNamedBiographyCards(people, gaps, suppressedCards, rules);
        var scored = DuplicateAnnotator.Annotate(
          new DrawResult { Targets = targets, Seed = null, LegendaryDiscards = [], SuppressedCards = suppressedCards },
            snapshot.MatchIndex, cancellationToken);
        var carded = targets.Select(t => t.Xref).ToHashSet(StringComparer.Ordinal);
        var withoutCards = people.Select(p => p.Xref).Where(x => !carded.Contains(x)).ToList();

        return Success(WantedFileWriter.ToJsonForXrefs(withoutCards, scored));
    }

    static CallToolResult Success(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CallToolResults.Success(document.RootElement.Clone(), CallToolResults.JsonOptions);
    }
}

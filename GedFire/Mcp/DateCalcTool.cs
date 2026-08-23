using System.Text.Json;
using GedCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// Pure date arithmetic over caller-supplied values. Unlike document tools,
// this has no DocumentSession and never reads the bound GEDCOM.
public sealed class DateCalcTool
{
    public const string ToolName = "date_calc";

    public const string Description =
        "Perform exact genealogical date arithmetic without reading the bound GEDCOM. Use normalize to resolve " +
        "a dual-dated year, add or sub to apply a y/m/d age to an exact Gregorian date, or diff to calculate " +
        "the elapsed canonical age between two exact Gregorian dates. Qualified, partial, BCE, ranged, and " +
        "non-Gregorian dates are rejected rather than having uncertainty or calendar semantics invented.";

    public const string InputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "description": "Arguments for one exact date calculation. operation determines which other fields are required and allowed.",
          "properties": {
            "operation": {
              "type": "string",
              "enum": ["normalize", "add", "sub", "diff"],
              "description": "normalize resolves the right-hand year of a dual date; add and sub apply age to date; diff returns the elapsed age from from to to."
            },
            "date": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "For normalize, an exact Gregorian D MON YYYY/YY dual date such as 11 FEB 1691/2. For add or sub, an exact Gregorian D MON YYYY date such as 29 JAN 1841."
            },
            "age": {
              "type": "string",
              "minLength": 2,
              "pattern": "^(?:[0-9]+y(?: [0-9]+m)?(?: [0-9]+d)?|[0-9]+m(?: [0-9]+d)?|[0-9]+d)$",
              "description": "For add or sub, a lowercase genealogical age in y, m, d order, such as 63y 4m 2d. Months must be 0-11 and days 0-30; omitted components are zero."
            },
            "from": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "For diff, the earlier exact Gregorian D MON YYYY date. It must not be after to."
            },
            "to": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "For diff, the later exact Gregorian D MON YYYY date."
            }
          },
          "required": ["operation"],
          "allOf": [
            {
              "if": { "properties": { "operation": { "const": "normalize" } }, "required": ["operation"] },
              "then": {
                "required": ["date"],
                "not": { "anyOf": [
                  { "required": ["age"] }, { "required": ["from"] }, { "required": ["to"] }
                ] }
              }
            },
            {
              "if": { "properties": { "operation": { "enum": ["add", "sub"] } }, "required": ["operation"] },
              "then": {
                "required": ["date", "age"],
                "not": { "anyOf": [ { "required": ["from"] }, { "required": ["to"] } ] }
              }
            },
            {
              "if": { "properties": { "operation": { "const": "diff" } }, "required": ["operation"] },
              "then": {
                "required": ["from", "to"],
                "not": { "anyOf": [ { "required": ["date"] }, { "required": ["age"] } ] }
              }
            }
          ]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "description": "The canonical result of one date calculation. Exactly one of date or age is non-null according to operation.",
          "properties": {
            "operation": {
              "type": "string",
              "enum": ["normalize", "add", "sub", "diff"],
              "description": "The operation that produced this result."
            },
            "date": {
              "type": ["string", "null"],
              "description": "The canonical D MON YYYY result for normalize, add, or sub; null for diff."
            },
            "age": {
              "type": ["string", "null"],
              "description": "The canonical y m d elapsed age for diff, including all three components; null for normalize, add, or sub."
            }
          },
          "required": ["operation", "date", "age"]
        }
        """;

    readonly ToolGate _gate;

    public DateCalcTool(ToolGate gate) =>
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

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

    Task<CallToolResult> InvokeAsync(
        string operation,
        string? date = null,
        string? age = null,
        string? from = null,
        string? to = null,
        CancellationToken cancellationToken = default) =>
        HandleAsync(operation, date, age, from, to, cancellationToken);

    public async Task<CallToolResult> HandleAsync(
        string operation,
        string? date,
        string? age,
        string? from,
        string? to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.RunAsync(
                _ => Task.FromResult(Execute(operation, date, age, from, to)),
                cancellationToken).ConfigureAwait(false);
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

    static CallToolResult Execute(string operation, string? date, string? age, string? from, string? to)
    {
        DateCalcResult result;
        try
        {
            result = operation switch
            {
                "normalize" => Normalize(date, age, from, to),
                "add" => AddOrSubtract(operation, date, age, from, to),
                "sub" => AddOrSubtract(operation, date, age, from, to),
                "diff" => Diff(date, age, from, to),
                _ => throw new ArgumentException(
                    $"operation must be normalize, add, sub, or diff; got '{operation}'.", nameof(operation)),
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return CallToolResults.Error(ex.Message);
        }

        return CallToolResults.Success(result, CallToolResults.JsonOptions);
    }

    static DateCalcResult Normalize(string? date, string? age, string? from, string? to)
    {
        if (age is not null || from is not null || to is not null)
            throw new ArgumentException("normalize accepts only date.");
        if (date is null)
            throw new ArgumentException("normalize requires date.");
        return new DateCalcResult("normalize", GedDate.NormalizeDualDate(date), null);
    }

    static DateCalcResult AddOrSubtract(
        string operation, string? date, string? age, string? from, string? to)
    {
        if (from is not null || to is not null)
            throw new ArgumentException($"{operation} accepts only date and age.");
        if (date is null || age is null)
            throw new ArgumentException($"{operation} requires date and age.");

        DateTime baseDate = GedDate.ParseExactGregorianDate(date);
        GedAge parsedAge = GedAge.Parse(age);
        DateTime calculated = operation == "add"
            ? GedDate.AddAge(baseDate, parsedAge)
            : GedDate.SubtractAge(baseDate, parsedAge);
        return new DateCalcResult(operation, GedDate.FormatExactGregorianDate(calculated), null);
    }

    static DateCalcResult Diff(string? date, string? age, string? from, string? to)
    {
        if (date is not null || age is not null)
            throw new ArgumentException("diff accepts only from and to.");
        if (from is null || to is null)
            throw new ArgumentException("diff requires from and to.");

        DateTime fromDate = GedDate.ParseExactGregorianDate(from);
        DateTime toDate = GedDate.ParseExactGregorianDate(to);
        return new DateCalcResult("diff", null, GedDate.Diff(fromDate, toDate).ToString());
    }
}
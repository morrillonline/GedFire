namespace GedFire;

/// <summary>
/// Minimal option parser shared by all CLI commands.  Replaces the per-command
/// hand-rolled loops, which had two defects: a flag in the last argument
/// position was never seen (the loops stopped at args.Length - 1), and a
/// value option followed by another option silently consumed the option name
/// as its value ("--input --output x" set input to "--output").
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _switches;

    /// <summary>Human-readable parse failure, or null when parsing succeeded.</summary>
    public string? Error { get; }

    private CommandLine(Dictionary<string, string> values, HashSet<string> switches,
                        string? error)
    {
        _values   = values;
        _switches = switches;
        Error     = error;
    }

    /// <summary>
    /// Parse <paramref name="args"/> against the declared option names.
    /// <paramref name="valueOptions"/> take a following value ("--input x");
    /// <paramref name="switches"/> stand alone ("--dry-run").  A repeated
    /// value option keeps the last occurrence.
    /// </summary>
    public static CommandLine Parse(string[] args,
                                    IReadOnlyCollection<string> valueOptions,
                                    IReadOnlyCollection<string>? switches = null)
    {
        var values  = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen    = new HashSet<string>(StringComparer.Ordinal);
        var allowedSwitches = switches ?? [];

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (valueOptions.Contains(arg))
            {
                if (i + 1 >= args.Length)
                    return Fail(values, seen, $"option {arg} requires a value");
                string value = args[++i];
                if (valueOptions.Contains(value) || allowedSwitches.Contains(value))
                    return Fail(values, seen, $"option {arg} requires a value, got option '{value}'");
                values[arg] = value;
            }
            else if (allowedSwitches.Contains(arg))
            {
                seen.Add(arg);
            }
            else
            {
                return Fail(values, seen, $"unknown option: {arg}");
            }
        }
        return new CommandLine(values, seen, null);
    }

    private static CommandLine Fail(Dictionary<string, string> values,
                                    HashSet<string> switches, string error) =>
        new(values, switches, error);

    /// <summary>The value of a value option, or null when it was not supplied.</summary>
    public string? Value(string name) =>
        _values.TryGetValue(name, out var v) ? v : null;

    /// <summary>Whether a switch was supplied.</summary>
    public bool Has(string name) => _switches.Contains(name);
}

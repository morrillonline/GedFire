using GedFire.Mcp;

namespace GedFire.Cli;

public sealed record FindPersonRequest(string Query, FindPersonHintsArgs? Hints, int MaxResults);

// ---------------------------------------------------------------------------
// Reads find-person's flags into a request, reporting the first flag whose
// value is not valid. Only supplied hints are present in the result, and a
// hint group exists only when at least one of its flags was given.
// ---------------------------------------------------------------------------

public static class FindPersonRequestReader
{
    const int DefaultMaxResults = 8;

    public static IReadOnlyCollection<string> Options { get; } =
    [
        "--input", "--query", "--max-results", "--sex",
        "--birth-year", "--birth-place", "--death-year", "--death-place",
        "--father", "--mother",
        "--spouse-name", "--marriage-year", "--marriage-place",
    ];

    public static bool TryRead(CommandLine commandLine, out FindPersonRequest? request, out string? error)
    {
        request = null;
        if (!TryReadInt(commandLine, "--max-results", DefaultMaxResults, out int maxResults, out error) ||
            !TryReadOptionalInt(commandLine, "--birth-year", out int? birthYear, out error) ||
            !TryReadOptionalInt(commandLine, "--death-year", out int? deathYear, out error) ||
            !TryReadOptionalInt(commandLine, "--marriage-year", out int? marriageYear, out error) ||
            !TryReadSex(commandLine, out string? sex, out error))
            return false;

        var birth = Event(birthYear, commandLine.Value("--birth-place"));
        var death = Event(deathYear, commandLine.Value("--death-place"));
        var marriage = Event(marriageYear, commandLine.Value("--marriage-place"));
        var parents = Parents(commandLine.Value("--father"), commandLine.Value("--mother"));
        var spouse = Spouse(commandLine.Value("--spouse-name"), marriage);

        var hints = sex is null && birth is null && death is null && parents is null && spouse is null
            ? null
            : new FindPersonHintsArgs { Sex = sex, Birth = birth, Death = death, Parents = parents, Spouse = spouse };
        request = new FindPersonRequest(commandLine.Require("--query"), hints, maxResults);
        return true;
    }

    static FindPersonEventHintArgs? Event(int? year, string? place) =>
        year is null && place is null ? null : new FindPersonEventHintArgs { Year = year, Place = place };

    static FindPersonParentsHintArgs? Parents(string? father, string? mother) =>
        father is null && mother is null ? null : new FindPersonParentsHintArgs { Father = father, Mother = mother };

    static FindPersonSpouseHintArgs? Spouse(string? name, FindPersonEventHintArgs? marriage) =>
        name is null && marriage is null ? null : new FindPersonSpouseHintArgs { Name = name, Marriage = marriage };

    static bool TryReadSex(CommandLine commandLine, out string? sex, out string? error)
    {
        error = null;
        sex = commandLine.Value("--sex")?.ToUpperInvariant();
        if (sex is null or "M" or "F") return true;

        error = $"--sex must be M or F, got: {commandLine.Value("--sex")}";
        return false;
    }

    static bool TryReadInt(CommandLine commandLine, string option, int defaultValue, out int value, out string? error)
    {
        error = null;
        value = defaultValue;
        string? raw = commandLine.Value(option);
        if (raw is null || int.TryParse(raw, out value)) return true;

        error = $"{option} must be an integer, got: {raw}";
        return false;
    }

    static bool TryReadOptionalInt(CommandLine commandLine, string option, out int? value, out string? error)
    {
        bool ok = TryReadInt(commandLine, option, 0, out int parsed, out error);
        value = ok && commandLine.Value(option) is not null ? parsed : null;
        return ok;
    }
}

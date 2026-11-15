namespace GedCore.Matching;

/// <summary>
/// Whether two GEDCOM names could be one person, by a rule simple enough to state in an error. Both names need the same
/// known surname; a blank surname, Unknown or underscores never matches. Given names are compared as whole words after
/// common abbreviations are expanded (Wm → William, Jas → James, …); a lone initial is not a given name.
/// </summary>
public static class NameAgreement
{
    static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["WM"] = "WILLIAM", ["JAS"] = "JAMES", ["GEO"] = "GEORGE", ["CHAS"] = "CHARLES", ["THOS"] = "THOMAS",
        ["JNO"] = "JOHN", ["ELIZ"] = "ELIZABETH", ["MARGT"] = "MARGARET", ["SAML"] = "SAMUEL", ["BENJ"] = "BENJAMIN",
        ["DANL"] = "DANIEL", ["ROBT"] = "ROBERT", ["NATH"] = "NATHANIEL", ["NATHL"] = "NATHANIEL", ["JOS"] = "JOSEPH",
        ["EDW"] = "EDWARD", ["HY"] = "HENRY",
    };

    sealed record ParsedName(string? Surname, IReadOnlyList<string> Givens);

    /// <summary>The same surname and at least one given name in common.</summary>
    public static bool SharesAGivenName(string? name, string? other) =>
        Parse(name) is { Surname: { } surname } a && Parse(other) is { } b && surname == b.Surname
        && a.Givens.Where(IsWord).Intersect(b.Givens.Where(IsWord)).Any();

    /// <summary>The same surname and the same first given name.</summary>
    public static bool SharesFirstGivenName(string? name, string? other) =>
        Parse(name) is { Surname: { } surname, Givens: [var first, ..] } && Parse(other) is { Givens: [var otherFirst, ..] } b
        && surname == b.Surname && first == otherFirst && !UnknownName.IsPlaceholder(first);

    /// <summary>The name as prose: GEDCOM's surname slashes removed.</summary>
    public static string Display(string? name) =>
        string.Join(' ', (name ?? "").Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static bool IsWord(string given) => given.Length > 1 && !UnknownName.IsPlaceholder(given);

    // A name with no slashes takes its last word as the surname.
    static ParsedName Parse(string? name)
    {
        var (given, surname) = GedNamePayload.Split(name);
        var givens = Words(given);
        if (surname is null && givens.Count > 0)
        {
            surname = givens[^1];
            givens.RemoveAt(givens.Count - 1);
        }
        string normalized = PersonNameNormalizer.Normalize(surname);
        bool unknown = UnknownName.IsPlaceholder(normalized) || normalized.All(c => c == '_');
        return new ParsedName(unknown ? null : normalized, [.. givens.Select(g => Abbreviations.GetValueOrDefault(g, g))]);
    }

    static List<string> Words(string text) => [.. PersonNameNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)];
}

namespace GedCore;

/// <summary>The GEDCOM 7 family events: they belong on a FAM record, never on an INDI.</summary>
public static class FamilyEvents
{
    public static readonly IReadOnlySet<string> Tags = new HashSet<string>(StringComparer.Ordinal)
    {
        "ANUL", "DIV", "DIVF", "ENGA", "MARB", "MARC", "MARL", "MARR", "MARS",
    };

    public static bool Contains(string? tag) => tag is not null && Tags.Contains(tag.ToUpperInvariant());
}

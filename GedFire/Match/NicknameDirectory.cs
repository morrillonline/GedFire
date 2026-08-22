using System.Text.Json;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Documented given-name equivalents (docs/design/mcp-server.md "Nickname
// dictionary"). Loads GedFire/Resources/nicknames.json once and answers one
// question: are two given names documented equivalents, given the
// candidate's recorded sex? Constructed from a Stream rather than reaching
// for the embedded resource itself, so tests can supply a small synthetic
// document instead of the full production dictionary.
// ---------------------------------------------------------------------------

public sealed class NicknameDirectory
{
    const string EmbeddedResourceName = "GedFire.Resources.nicknames.json";

    readonly List<HashSet<string>> _maleGroups;
    readonly List<HashSet<string>> _femaleGroups;

    /// <summary>
    /// Load groups from a JSON document shaped like nicknames.json: an
    /// object with "male" and "female" properties, each an object mapping a
    /// group label to an array of documented-equivalent given names. Every
    /// entry is normalized through <see cref="PersonNameNormalizer"/> so
    /// lookups share the rest of the matcher's rules.
    /// </summary>
    public NicknameDirectory(Stream nicknamesJson)
    {
        ArgumentNullException.ThrowIfNull(nicknamesJson);
        using var doc = JsonDocument.Parse(nicknamesJson);
        _maleGroups = LoadGroups(doc.RootElement, "male");
        _femaleGroups = LoadGroups(doc.RootElement, "female");
    }

    /// <summary>Load the production dictionary embedded in this assembly.</summary>
    public static NicknameDirectory LoadEmbedded()
    {
        var assembly = typeof(NicknameDirectory).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource not found: {EmbeddedResourceName}. " +
                "Is it marked <EmbeddedResource> in GedFire.csproj?");
        return new NicknameDirectory(stream);
    }

    /// <summary>
    /// Are <paramref name="givenA"/> and <paramref name="givenB"/> documented
    /// equivalents — any group whose list contains both, normalized? The
    /// lists include the formal name itself, so this covers formal-to-nickname
    /// and nickname-to-nickname equivalence without special-casing either.
    /// <paramref name="isMale"/> selects which map to consult: true for male,
    /// false for female, null (sex unrecorded) consults both.
    /// </summary>
    public bool AreEquivalent(string? givenA, string? givenB, bool? isMale)
    {
        string a = PersonNameNormalizer.Normalize(givenA);
        string b = PersonNameNormalizer.Normalize(givenB);
        if (a.Length == 0 || b.Length == 0) return false;

        if (isMale != false && GroupsContainBoth(_maleGroups, a, b)) return true;
        if (isMale != true && GroupsContainBoth(_femaleGroups, a, b)) return true;
        return false;
    }

    static bool GroupsContainBoth(List<HashSet<string>> groups, string a, string b)
    {
        foreach (var group in groups)
            if (group.Contains(a) && group.Contains(b))
                return true;
        return false;
    }

    static List<HashSet<string>> LoadGroups(JsonElement root, string mapName)
    {
        var groups = new List<HashSet<string>>();
        if (!root.TryGetProperty(mapName, out var map) || map.ValueKind != JsonValueKind.Object)
            return groups;

        foreach (var group in map.EnumerateObject())
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (group.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in group.Value.EnumerateArray())
                {
                    string normalized = PersonNameNormalizer.Normalize(item.GetString());
                    if (normalized.Length > 0) names.Add(normalized);
                }
            }
            if (names.Count > 0) groups.Add(names);
        }
        return groups;
    }
}

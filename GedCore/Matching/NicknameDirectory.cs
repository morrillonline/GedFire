using System.Text.Json;

namespace GedCore.Matching;

// ---------------------------------------------------------------------------
// Documented given-name equivalents, embedded resource and all, shared so
// the duplicate-person detector inside GedCore.Apply uses the same nickname
// equivalence GedFire's find_person tool does. Answers one
// question: are two given names documented equivalents, given the
// candidate's recorded sex? Constructed from a Stream rather than reaching
// for the embedded resource itself, so tests can supply a small synthetic
// document instead of the full production dictionary.
// ---------------------------------------------------------------------------

public sealed class NicknameDirectory
{
    const string EmbeddedResourceName = "GedCore.Resources.nicknames.json";

    readonly List<NicknameGroup> _maleGroups;
    readonly List<NicknameGroup> _femaleGroups;

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
                "Is it marked <EmbeddedResource> in GedCore.csproj?");
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

    static bool GroupsContainBoth(List<NicknameGroup> groups, string a, string b)
    {
        foreach (var group in groups)
            if (group.Names.Contains(a) && group.Names.Contains(b))
                return true;
        return false;
    }

    /// <summary>
    /// Is <paramref name="nickname"/> a documented nickname of <paramref name="formal"/>: a different name listed
    /// in the group labelled with the formal name? "Bill" is a nickname of "William"; "Bob" is not, and "William"
    /// is not a nickname of "Bill". <paramref name="isMale"/> selects the map as in <see cref="AreEquivalent"/>.
    /// </summary>
    public bool IsNicknameOf(string? nickname, string? formal, bool? isMale)
    {
        string nick = PersonNameNormalizer.Normalize(nickname);
        string full = PersonNameNormalizer.Normalize(formal);
        if (nick.Length == 0 || full.Length == 0 || nick == full) return false;

        bool Listed(List<NicknameGroup> groups) => groups.Any(g => g.Label == full && g.Names.Contains(nick));
        return (isMale != false && Listed(_maleGroups)) || (isMale != true && Listed(_femaleGroups));
    }

    static List<NicknameGroup> LoadGroups(JsonElement root, string mapName)
    {
        var groups = new List<NicknameGroup>();
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
            if (names.Count > 0) groups.Add(new NicknameGroup(PersonNameNormalizer.Normalize(group.Name), names));
        }
        return groups;
    }

    sealed record NicknameGroup(string Label, HashSet<string> Names);
}

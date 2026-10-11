using System.Text.Json;
using System.Text.RegularExpressions;

namespace GedCore.Policy;

/// <summary>The verification-note rule: a fact graded <see cref="MaxQuay"/> or lower must be named by a note containing <see cref="NotePrefix"/>.</summary>
public sealed record VerificationNoteRule(int MaxQuay, string NotePrefix);

/// <summary>
/// The editorial rules a server enforces on every changeset it validates or applies.
/// Loaded from a JSON file; every key is optional and an absent rule is not enforced:
/// <c>bannedPhrases</c>, <c>bannedPatterns</c> (regular expressions),
/// <c>verificationNote</c> {<c>maxQuay</c>, <c>notePrefix</c>}, and
/// <c>deathPlaceEvidence</c> (a regular expression).
/// </summary>
public sealed class ChangesetPolicy
{
    static readonly string[] KnownKeys = ["bannedPhrases", "bannedPatterns", "verificationNote", "deathPlaceEvidence"];

    public IReadOnlyList<string> BannedPhrases { get; private init; } = [];
    public IReadOnlyList<string> BannedPatterns { get; private init; } = [];
    public VerificationNoteRule? VerificationNote { get; private init; }
    public string? DeathPlaceEvidence { get; private init; }

    IReadOnlyList<IPolicyRule> _rules = [];

    /// <exception cref="FormatException">The policy is not valid JSON, names an unknown key, or holds a malformed rule.</exception>
    public static ChangesetPolicy Load(Stream stream)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(stream).RootElement; }
        catch (JsonException ex) { throw new FormatException($"policy file is not valid JSON: {ex.Message}"); }

        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("policy file must be a JSON object.");
        foreach (var property in root.EnumerateObject().Where(p => !KnownKeys.Contains(p.Name)))
            throw new FormatException($"policy file has unknown key \"{property.Name}\"; accepted keys: {string.Join(", ", KnownKeys)}.");

        var policy = new ChangesetPolicy
        {
            BannedPhrases = ReadStrings(root, "bannedPhrases"),
            BannedPatterns = ReadStrings(root, "bannedPatterns"),
            VerificationNote = ReadVerificationNote(root),
            DeathPlaceEvidence = ReadString(root, "deathPlaceEvidence"),
        };
        policy._rules = policy.BuildRules();
        return policy;
    }

    /// <summary>Every violation of the policy in the selected items of a changeset's JSON (all items when none are given); empty when it complies.</summary>
    public IReadOnlyList<string> Check(string changesetJson, IReadOnlySet<int>? selectedItems = null)
    {
        using var document = JsonDocument.Parse(changesetJson);
        var items = PolicyItem.Read(document.RootElement, selectedItems);
        return [.. _rules.SelectMany(rule => rule.Check(items)).Select(v => $"policy: {v}")];
    }

    List<IPolicyRule> BuildRules()
    {
        var rules = new List<IPolicyRule>();
        if (BannedPhrases.Count > 0 || BannedPatterns.Count > 0)
            rules.Add(new BannedProseRule(BannedPhrases, [.. BannedPatterns.Select(p => Compile("bannedPatterns", p))]));
        if (VerificationNote is { } note) rules.Add(new LowQuayNoteRule(note.MaxQuay, note.NotePrefix));
        if (DeathPlaceEvidence is { } evidence) rules.Add(new DeathPlaceEvidenceRule(Compile("deathPlaceEvidence", evidence)));
        return rules;
    }

    static Regex Compile(string key, string pattern)
    {
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (ArgumentException ex) { throw new FormatException($"policy \"{key}\" holds an invalid regular expression \"{pattern}\": {ex.Message}"); }
    }

    static List<string> ReadStrings(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var element)) return [];
        if (element.ValueKind != JsonValueKind.Array
            || element.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(e.GetString())))
            throw new FormatException($"policy \"{key}\" must be an array of non-blank strings.");
        return [.. element.EnumerateArray().Select(e => e.GetString()!)];
    }

    static string? ReadString(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var element)) return null;
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            throw new FormatException($"policy \"{key}\" must be a non-blank string.");
        return element.GetString();
    }

    static VerificationNoteRule? ReadVerificationNote(JsonElement root)
    {
        if (!root.TryGetProperty("verificationNote", out var element)) return null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("maxQuay", out var quay) || !quay.TryGetInt32(out int maxQuay) || maxQuay is < 0 or > 3
            || ReadString(element, "notePrefix") is not { } prefix)
            throw new FormatException("policy \"verificationNote\" must be {\"maxQuay\": 0-3, \"notePrefix\": \"...\"}.");
        return new VerificationNoteRule(maxQuay, prefix);
    }
}

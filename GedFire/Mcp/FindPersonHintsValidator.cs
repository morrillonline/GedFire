using System.Text.Json;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Checks find_person's hints once they are typed: nothing empty or blank, years
// in range, and no unrecognized property at any level. Shared with find_family,
// which accepts the same birth and death hints.
// ---------------------------------------------------------------------------

public static class FindPersonHintsValidator
{
    public static bool TryValidate(FindPersonHintsArgs? hints, out string? error)
    {
        if (hints is null)
        {
            error = null;
            return true;
        }

        if (TryUnknownProperty("hints", hints.AdditionalProperties, out error)) return false;
        if (hints.Sex is null && hints.Birth is null && hints.Death is null && hints.Parents is null && hints.Spouse is null)
        {
            error = "hints must contain at least one of sex, birth, death, parents, or spouse.";
            return false;
        }

        if (hints.Sex is not (null or "M" or "F"))
        {
            error = "hints.sex must be \"M\" or \"F\".";
            return false;
        }

        if (!TryValidateEvent("hints.birth", hints.Birth, out error) ||
            !TryValidateEvent("hints.death", hints.Death, out error))
            return false;

        if (hints.Parents is { } parents)
        {
            if (TryUnknownProperty("hints.parents", parents.AdditionalProperties, out error)) return false;
            if (parents.Father is null && parents.Mother is null)
            {
                error = "hints.parents must contain father or mother.";
                return false;
            }
            if (!TryValidateText("hints.parents.father", parents.Father, out error) ||
                !TryValidateText("hints.parents.mother", parents.Mother, out error))
                return false;
        }

        if (hints.Spouse is { } spouse)
        {
            if (TryUnknownProperty("hints.spouse", spouse.AdditionalProperties, out error)) return false;
            if (spouse.Name is null && spouse.Marriage is null)
            {
                error = "hints.spouse must contain name or marriage.";
                return false;
            }
            if (!TryValidateText("hints.spouse.name", spouse.Name, out error) ||
                !TryValidateEvent("hints.spouse.marriage", spouse.Marriage, out error))
                return false;
        }

        error = null;
        return true;
    }

    static bool TryValidateEvent(string path, FindPersonEventHintArgs? hint, out string? error)
    {
        if (hint is null)
        {
            error = null;
            return true;
        }
        if (TryUnknownProperty(path, hint.AdditionalProperties, out error)) return false;
        if (hint.Year is null && hint.Place is null)
        {
            error = $"{path} must contain year or place.";
            return false;
        }
        if (hint.Year is < 1 or > 9999)
        {
            error = $"{path}.year must be between 1 and 9999.";
            return false;
        }
        return TryValidateText($"{path}.place", hint.Place, out error);
    }

    static bool TryValidateText(string path, string? value, out string? error)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            error = $"{path} must not be blank.";
            return false;
        }
        error = null;
        return true;
    }

    static bool TryUnknownProperty(
        string path, Dictionary<string, JsonElement>? additionalProperties, out string? error)
    {
        if (additionalProperties is { Count: > 0 })
        {
            error = $"{path} contains unknown property '{additionalProperties.Keys.First()}'.";
            return true;
        }
        error = null;
        return false;
    }

}

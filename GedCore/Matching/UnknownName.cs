namespace GedCore.Matching;

/// <summary>
/// A name part written Unknown means the part is not known; it is not a name. Both spellings occur
/// after normalization: the word itself, and the display placeholder GedIndividual substitutes for it.
/// </summary>
public static class UnknownName
{
    public static bool IsPlaceholder(string? normalizedPart) => normalizedPart is "UNKNOWN" or "____";
}

namespace GedCore.Matching;

/// <summary>
/// A name part written Unknown means the part is not known; it is not a name. So is a part with
/// nothing left after normalization (a blank, or only punctuation such as ---- or ?). Both spellings
/// of the word occur after normalization: the word itself, and the display placeholder
/// GedIndividual substitutes for it.
/// </summary>
public static class UnknownName
{
    public static bool IsPlaceholder(string? normalizedPart) => string.IsNullOrEmpty(normalizedPart) || normalizedPart is "UNKNOWN" or "____";
}

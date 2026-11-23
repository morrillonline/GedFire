using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// The scope of a surname study, on the assumption that a child takes the father's surname. Someone who bears a study
/// surname gets every card; a spouse of such a person who does not bear one gets only Enrich person; anyone else gets
/// none. A family's New child card is dealt, to either spouse, only when its husband bears a study surname.
/// </summary>
public sealed class StudyScope
{
    /// <summary>Every reason this scope gives starts with this, so a caller can tell a scope withholding from the other rules.</summary>
    public const string ReasonPrefix = "out of study scope: ";

    readonly HashSet<string> _surnames;

    public StudyScope(IEnumerable<string> surnames) =>
        _surnames = new HashSet<string>(surnames.Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);

    public bool Bears(GedIndividual person) => _surnames.Contains(person.LastNameRaw);

    public bool BearsOrMarriedOne(GedIndividual person) =>
        Bears(person) || person.FamSpouse.Any(family => family.SpouseOf(person) is { } spouse && Bears(spouse));

    /// <summary>
    /// Why the card is outside the scope, or null when it is inside. A New child card is judged on
    /// <paramref name="family"/>; with no family given only the person is judged.
    /// </summary>
    public string? WithheldReason(GedIndividual person, CardType cardType, GedFamily? family = null)
    {
        if (!BearsOrMarriedOne(person))
            return ReasonPrefix + "bears no study surname and is not married to someone who does";
        if (cardType == CardType.NewChild)
            return family is null || HusbandBears(family)
                ? null
                : ReasonPrefix + $"{family.Xref} has no husband who bears a study surname, so its children would not bear one";
        if (Bears(person) || cardType == CardType.EnrichPerson) return null;
        return ReasonPrefix + "bears no study surname; the spouse of someone who does is dealt only Enrich person, and New child for a family whose husband bears one";
    }

    public bool HusbandBears(GedFamily family) => family.Husband is { } husband && Bears(husband);
}

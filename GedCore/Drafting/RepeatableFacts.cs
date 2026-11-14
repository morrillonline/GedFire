using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>
/// Places a fact of a repeatable tag (CENS, RESI, OCCU…) among the instances a record already holds, by its date:
/// a date no instance has is added, a date one instance has updates that instance, and a date several have is refused.
/// Placing by date keeps a drafted op landing where it was meant to after another changeset adds instances of the tag.
/// </summary>
internal static class RepeatableFacts
{
    public static bool IsRepeatable(string tag) => !CreateOrUpdateVitalOp.IsSingleValued(tag);

    /// <summary>
    /// The one held instance a fact with this date updates, or null when the fact is added. A fact with no date, or with
    /// substructures (the held ones are not known), is always added.
    /// </summary>
    public static ExistingFact? InstanceOnDate(IReadOnlyList<ExistingFact> held, string? date, bool hasSubstructures)
    {
        if (date is null || hasSubstructures) return null;
        var onDate = OnDate(held, date);
        return onDate.Count == 1 ? onDate[0] : null;
    }

    /// <summary>The refusal for each fact whose date several held instances have, so the draft cannot tell which it means.</summary>
    public static IEnumerable<string> Errors(IReadOnlyList<DraftFinding> findings, IRecordHistory? history)
    {
        if (history is null) yield break;
        for (int f = 0; f < findings.Count; f++)
            for (int i = 0; i < findings[f].Facts.Count; i++)
            {
                var fact = findings[f].Facts[i];
                if (!IsPlacedByDate(fact)) continue;
                var onDate = OnDate(history.FactsOn(fact.Record, fact.Tag), fact.Date!);
                if (onDate.Count > 1) yield return AmbiguousDate($"findings[{f}].facts[{i}]", fact, onDate);
            }
    }

    static bool IsPlacedByDate(DraftFact fact) =>
        fact.Withdraw is null && !fact.CiteOnly && IsRepeatable(fact.Tag) && fact.Date is not null && fact.Substructures.Count == 0;

    // A bare year names every plain date in that year.
    static List<ExistingFact> OnDate(IReadOnlyList<ExistingFact> held, string date) =>
        [.. held.Where(e => e.Date is not null && (Specificity.SameDate(e.Date, date) || Specificity.InYear(e.Date, date)))];

    static string AmbiguousDate(string path, DraftFact fact, IReadOnlyList<ExistingFact> onDate) =>
        $"{path} {fact.Tag} on {fact.Record} dated {fact.Date} matches {onDate.Count} existing {fact.Tag} facts " +
        $"({string.Join("; ", onDate.Select(Describe))}); give the full date of one, or merge the duplicates in the tree first.";

    static string Describe(ExistingFact fact) => fact.Place is null ? fact.Date! : $"{fact.Date} at {fact.Place}";
}

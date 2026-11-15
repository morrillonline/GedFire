using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>
/// Refuses a family event (MARR, DIV, …) asserted on a person: it belongs on the couple's family. Withdrawing one a
/// person already holds is the only use allowed, so a misplaced event can be removed.
/// </summary>
internal static class FamilyEventPlacement
{
    public static IEnumerable<string> Errors(IReadOnlyList<DraftFinding> findings, IPersonNames? names)
    {
        var newPeople = findings.SelectMany(f => f.People).Select(p => p.Xref).ToHashSet(StringComparer.Ordinal);
        for (int f = 0; f < findings.Count; f++)
            for (int i = 0; i < findings[f].Facts.Count; i++)
            {
                var fact = findings[f].Facts[i];
                if (fact.Withdraw is not null || !FamilyEvents.Contains(fact.Tag)) continue;
                bool isPerson = Placeholder.IsPlaceholder(fact.Record)
                    ? newPeople.Contains(fact.Record)
                    : names?.HasPerson(fact.Record) == true;
                if (isPerson)
                    yield return $"findings[{f}].facts[{i}].record {fact.Record} is a person, but {fact.Tag} is a family event; " +
                                 "record it on the couple's family instead (the family xref, or a spouse relation's family).";
            }
    }
}

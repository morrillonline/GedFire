using GedCore.Matching;

namespace GedCore.Packages;

/// <summary>
/// Two new people in one package whose names share the first given name and the surname are almost always one person
/// named two ways in two findings, so they are refused unless the caller declares the pair in sameNamePeople.
/// </summary>
internal static class OneNamePerPersonRule
{
    public static IEnumerable<string> Check(PackageContents package, IReadOnlyList<SameNamePair> declared)
    {
        foreach (string xref in declared.SelectMany(p => new[] { p.First, p.Second }).Distinct()
                     .Where(x => package.People.All(p => p.Xref != x)))
            yield return $"sameNamePeople names {xref}, which no changeset in the package creates";

        var people = package.People;
        for (int i = 0; i < people.Count; i++)
            for (int j = i + 1; j < people.Count; j++)
            {
                var (a, b) = (people[i], people[j]);
                if (!NameAgreement.SharesFirstGivenName(a.Name, b.Name) || declared.Any(p => p.Covers(a.Xref, b.Xref))) continue;
                yield return $"{a.Where} creates {a.Xref} \"{NameAgreement.Display(a.Name)}\" and {b.Where} creates {b.Xref} " +
                             $"\"{NameAgreement.Display(b.Name)}\": two new people with the same first given name and surname are " +
                             "almost always one person named two ways; give the person one name in every finding, or, if they are " +
                             $"truly two people, declare them in sameNamePeople: [[\"{a.Xref}\", \"{b.Xref}\"]]";
            }
    }
}

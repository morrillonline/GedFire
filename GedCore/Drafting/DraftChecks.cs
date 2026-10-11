namespace GedCore.Drafting;

/// <summary>The refusals a draft makes before compiling: a QUAY the evidence cannot support, and a target name that is not the person's.</summary>
internal static class DraftChecks
{
    public static IEnumerable<string> Check(IReadOnlyList<DraftFinding> findings, IPersonNames names)
    {
        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            foreach (var error in TargetNameErrors(finding, $"findings[{f}]", names)) yield return error;
            for (int i = 0; i < finding.Facts.Count; i++)
                if (QuayError(finding, finding.Facts[i], $"findings[{f}].facts[{i}]") is { } error) yield return error;
        }
    }

    /// <summary>The highest QUAY the source class and information class can support.</summary>
    public static int Ceiling(string sourceClass, string information) => sourceClass switch
    {
        "original" or "image copy" => information == "primary" ? 3 : 2,
        "derivative" => information == "primary" ? 2 : 1,
        "authored" => 1,
        _ => 0,
    };

    static string? QuayError(DraftFinding finding, DraftFact fact, string path)
    {
        string cls = fact.Class ?? finding.Source.Class;
        string info = fact.Information ?? finding.Source.Information;
        int ceiling = Ceiling(cls, info);
        return fact.Quay <= ceiling ? null
            : $"{path}.quay is {fact.Quay}, but a {cls} source with {info} information supports at most {ceiling}.";
    }

    static IEnumerable<string> TargetNameErrors(DraftFinding finding, string path, IPersonNames names)
    {
        if (finding.Target.Name is not { } named || names.PersonName(finding.Target.Xref) is not { } actual) yield break;
        if (!string.Equals(Fold(named), Fold(actual), StringComparison.OrdinalIgnoreCase))
            yield return $"{path}.target.name \"{named}\" differs from the name \"{actual}\" recorded for {finding.Target.Xref}.";
    }

    static string Fold(string name) => string.Join(' ', name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

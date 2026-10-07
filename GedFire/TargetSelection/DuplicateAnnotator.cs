using GedFire.Match;

namespace GedFire.TargetSelection;

/// <summary>Fills each drawn target's <see cref="SelectionTarget.PossibleDuplicateOf"/> from the GEN301 duplicate test.</summary>
public static class DuplicateAnnotator
{
    // A researcher checks a few leading candidates by hand; a longer list is noise, and the count says more exist.
    public const int MaxListedDuplicates = 3;

    public static DrawResult Annotate(DrawResult draw, MatchIndex index, CancellationToken cancellationToken = default)
    {
        var drawn = draw.Targets.Select(t => t.Xref).ToHashSet(StringComparer.Ordinal);
        var pairs = PersonMatcher.FindPossibleDuplicates(index, drawn, cancellationToken);

        var duplicates = new Dictionary<string, List<PossibleDuplicateEntry>>(StringComparer.Ordinal);
        void Add(string target, string other, double score)
        {
            if (!drawn.Contains(target)) return;
            if (!duplicates.TryGetValue(target, out var list)) duplicates[target] = list = [];
            list.Add(new PossibleDuplicateEntry(other, Math.Round(score, 1)));
        }

        foreach (var pair in pairs)
        {
            Add(pair.FirstId, pair.SecondId, pair.Score);
            Add(pair.SecondId, pair.FirstId, pair.Score);
        }

        return draw with
        {
            Targets = [.. draw.Targets.Select(t => Annotate(t, duplicates))],
        };
    }

    static SelectionTarget Annotate(SelectionTarget target, Dictionary<string, List<PossibleDuplicateEntry>> duplicates)
    {
        if (!duplicates.TryGetValue(target.Xref, out var list)) return target;

        var best = list.OrderByDescending(e => e.Score).ThenBy(e => e.Xref, StringComparer.Ordinal);
        return target with { PossibleDuplicateOf = [.. best.Take(MaxListedDuplicates)], PossibleDuplicateCount = list.Count };
    }
}

using GedFire.Match;

namespace GedFire.TargetSelection;

/// <summary>Fills each drawn target's <see cref="SelectionTarget.PossibleDuplicateOf"/> from the GEN301 duplicate test.</summary>
public static class DuplicateAnnotator
{
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
            Targets = [.. draw.Targets.Select(t => t with
            {
                PossibleDuplicateOf = duplicates.TryGetValue(t.Xref, out var list)
                    ? [.. list.OrderByDescending(e => e.Score).ThenBy(e => e.Xref, StringComparer.Ordinal)]
                    : [],
            })],
        };
    }
}

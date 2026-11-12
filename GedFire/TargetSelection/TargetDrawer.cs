using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>One target discarded from a draw because its person would have been a second Legendary person in the pack.</summary>
public sealed record DiscardedTarget
{
    public required string Xref { get; init; }
    public required string Name { get; init; }
    public required string CardType { get; init; }
}

/// <summary>The outcome of one draw: the pack, the seed that produced it (null when nothing was drawn — the targets were requested explicitly), and any Legendary-cap discards.</summary>
public sealed record DrawResult
{
    public required IReadOnlyList<SelectionTarget> Targets { get; init; }
    public required long? Seed { get; init; }
    public required IReadOnlyList<DiscardedTarget> LegendaryDiscards { get; init; }
    public IReadOnlyList<SuppressedCard> SuppressedCards { get; init; } = [];
}

/// <summary>
/// Draws a pack from the full candidate set by person: uniform odds for every
/// person with at least one card, and every card of each drawn person, kept
/// together. A pack holds at most one person with a Legendary-band card; a
/// later such person is discarded whole, not replaced by a redraw, so the pack
/// can land short of the requested number of people.
/// </summary>
public static class TargetDrawer
{
    /// <summary>
    /// <see cref="Draw"/> over the gap cards in <paramref name="model"/>, with every card that pays for each
    /// drawn person: their gap cards, then a man's Biography card. Biography alone never brings a person into the draw.
    /// </summary>
    public static DrawResult DrawPack(GedModel model, IReadOnlyList<SelectionTarget> gapCards, int people, long seed)
    {
        var draw = Draw(gapCards, people, seed);
        var drawn = draw.Targets.Select(t => t.Xref).Distinct().Select(xref => model.Individuals[xref]);
        return draw with { Targets = GapDetector.WithBiographyCards(drawn, draw.Targets) };
    }

    /// <summary>
    /// Draw <paramref name="people"/> people uniformly at random, without
    /// replacement, from those holding <paramref name="candidates"/>, and return
    /// all of each one's cards. <paramref name="seed"/>
    /// is recorded verbatim in the result for the audit log — callers
    /// generate it themselves, typically from the system clock. The CLI has
    /// no --seed input, so users cannot request a specific outcome.
    /// </summary>
    public static DrawResult Draw(IReadOnlyList<SelectionTarget> candidates, int people, long seed)
    {
        var rng = new Random(unchecked((int)seed));
        var pool = candidates.GroupBy(c => c.Xref, StringComparer.Ordinal).Select(g => g.ToList()).ToList();
        int take = Math.Max(0, Math.Min(people, pool.Count));

        // Partial Fisher-Yates: selects `take` people uniformly at random,
        // without replacement, and leaves them in the order they were dealt
        // (indices 0..take-1) — that draw order is what "keep the first
        // Legendary person, discard the rest" below means by "first".
        for (int i = 0; i < take; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var drawn = new List<SelectionTarget>();
        var discards = new List<DiscardedTarget>();
        bool haveLegendary = false;

        foreach (var cards in pool.Take(take))
        {
            if (cards.Any(c => c.Difficulty.Band == DifficultyBand.Legendary))
            {
                if (haveLegendary)
                {
                    discards.AddRange(cards.Select(c => new DiscardedTarget { Xref = c.Xref, Name = c.Name, CardType = c.CardType }));
                    continue;
                }
                haveLegendary = true;
            }
            drawn.AddRange(cards);
        }

        return new DrawResult { Targets = drawn, Seed = seed, LegendaryDiscards = discards };
    }
}

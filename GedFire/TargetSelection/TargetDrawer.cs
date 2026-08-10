namespace GedFire.TargetSelection;

/// <summary>One target discarded from a draw because it would have been a second Legendary in the pack.</summary>
public sealed record DiscardedTarget
{
    public required string Xref { get; init; }
    public required string Name { get; init; }
    public required string CardType { get; init; }
}

/// <summary>The outcome of one draw: the pack, the seed that produced it, and any Legendary-cap discards.</summary>
public sealed record DrawResult
{
    public required IReadOnlyList<SelectionTarget> Targets { get; init; }
    public required long Seed { get; init; }
    public required IReadOnlyList<DiscardedTarget> LegendaryDiscards { get; init; }
}

/// <summary>
/// Draws a pack of targets from the full candidate set: uniform odds for
/// every card, with a hard cap of one Legendary-band card per pack
/// (docs/design/target-selection.md, "Draw — equal odds for every card,
/// one hard exception"). A second Legendary in the draw is discarded, not
/// replaced by a redraw, so the pack can land short of the requested count.
/// </summary>
public static class TargetDrawer
{
    /// <summary>
    /// Draw <paramref name="count"/> candidates uniformly at random, without
    /// replacement, from <paramref name="candidates"/>. <paramref name="seed"/>
    /// is recorded verbatim in the result for the audit log — callers
    /// generate it themselves (e.g. from the system clock); this design has
    /// deliberately no --seed input, so it is never supplied by a caller
    /// wanting a specific outcome.
    /// </summary>
    public static DrawResult Draw(IReadOnlyList<SelectionTarget> candidates, int count, long seed)
    {
        var rng = new Random(unchecked((int)seed));
        var pool = candidates.ToList();
        int take = Math.Max(0, Math.Min(count, pool.Count));

        // Partial Fisher-Yates: selects `take` elements uniformly at random,
        // without replacement, and leaves them in the order they were dealt
        // (indices 0..take-1) — that draw order is what "keep the first
        // Legendary, discard the rest" below means by "first".
        for (int i = 0; i < take; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var drawn = new List<SelectionTarget>(take);
        var discards = new List<DiscardedTarget>();
        bool haveLegendary = false;

        for (int i = 0; i < take; i++)
        {
            var candidate = pool[i];
            if (candidate.Difficulty.Band == DifficultyBand.Legendary)
            {
                if (haveLegendary)
                {
                    discards.Add(new DiscardedTarget
                    {
                        Xref = candidate.Xref,
                        Name = candidate.Name,
                        CardType = candidate.CardType,
                    });
                    continue;
                }
                haveLegendary = true;
            }
            drawn.Add(candidate);
        }

        return new DrawResult { Targets = drawn, Seed = seed, LegendaryDiscards = discards };
    }
}

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
/// Draws a pack from the full candidate set by person, keeping every card of each drawn person together. Each person
/// is drawn with odds proportional to their weight: one, plus one for each of their cards withheld only until
/// enrichment writes an anchor, since enriching that person also unlocks those cards for the next pack. A pack holds
/// at most one person with a Legendary-band card; a later such person is passed over and the next person drawn takes
/// the seat. It seats at most <see cref="EnrichOnlyPeoplePerPack"/> people whose only card is Enrich person while
/// people with a relationship card remain; the rest are set aside to fill seats no one else can. The pack is short
/// only when the pool runs out.
/// </summary>
public static class TargetDrawer
{
    /// <summary>
    /// <see cref="Draw"/> over the gap cards in <paramref name="model"/>, with every card that pays for each
    /// drawn person: their gap cards, then a man's Biography card. Biography alone never brings a person into the draw.
    /// The result carries <paramref name="suppressedCards"/>, the cards withheld while detecting the gap cards; of those
    /// a study scope withheld, only the drawn people's, a Biography card among them. A person's withheld cards that
    /// enrichment would unlock weigh their odds.
    /// </summary>
    public static DrawResult DrawPack(
        GedModel model, IReadOnlyList<SelectionTarget> gapCards, int people, long seed,
        IReadOnlyCollection<SuppressedCard>? suppressedCards = null, GapRules? rules = null)
    {
        var weights = (suppressedCards ?? [])
            .Where(card => card.UnlocksOnEnrichment)
            .GroupBy(card => card.Xref, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => 1 + g.Count(), StringComparer.Ordinal);
        var draw = Draw(gapCards, people, seed, weights);
        var drawnXrefs = draw.Targets.Select(t => t.Xref).Distinct().ToList();
        var withheld = (suppressedCards ?? []).Where(card => !card.OutOfScope || drawnXrefs.Contains(card.Xref)).ToList();
        var targets = GapDetector.WithBiographyCards(drawnXrefs.Select(xref => model.Individuals[xref]), draw.Targets, withheld, rules);
        return draw with { Targets = targets, SuppressedCards = withheld };
    }

    /// <summary>
    /// Draw <paramref name="people"/> people at random, without replacement, from those holding
    /// <paramref name="candidates"/>, each with odds proportional to their weight in <paramref name="weights"/>
    /// (1 when absent), and return all of each one's cards. <paramref name="seed"/>
    /// is recorded verbatim in the result for the audit log — callers
    /// generate it themselves, typically from the system clock. The CLI has
    /// no --seed input, so users cannot request a specific outcome.
    /// </summary>
    public static DrawResult Draw(
        IReadOnlyList<SelectionTarget> candidates, int people, long seed, IReadOnlyDictionary<string, int>? weights = null)
    {
        var rng = new Random(unchecked((int)seed));
        var pool = candidates.GroupBy(c => c.Xref, StringComparer.Ordinal).Select(g => g.ToList()).ToList();

        // Weighted random order without replacement (Efraimidis-Spirakis): each person's key is u^(1/weight), highest
        // first. Equal weights give a uniform shuffle. Keys are drawn in pool order so a seed always gives the same order.
        var order = pool
            .Select(cards => (Cards: cards, Key: Math.Pow(rng.NextDouble(), 1.0 / Math.Max(1, weights?.GetValueOrDefault(cards[0].Xref) ?? 1))))
            .OrderByDescending(p => p.Key)
            .Select(p => p.Cards);

        var drawn = new List<SelectionTarget>();
        var discards = new List<DiscardedTarget>();
        var setAside = new List<List<SelectionTarget>>();
        bool haveLegendary = false;
        int seated = 0, enrichOnlySeated = 0;

        // Seats a person unless the pack already holds its Legendary person; a later one is passed over whole.
        void Seat(List<SelectionTarget> cards)
        {
            if (cards.Any(c => c.Difficulty.Band == DifficultyBand.Legendary))
            {
                if (haveLegendary)
                {
                    discards.AddRange(cards.Select(c => new DiscardedTarget { Xref = c.Xref, Name = c.Name, CardType = c.CardType }));
                    return;
                }
                haveLegendary = true;
            }
            drawn.AddRange(cards);
            seated++;
            if (IsEnrichOnly(cards)) enrichOnlySeated++;
        }

        foreach (var cards in order)
        {
            if (seated == people) break;
            if (IsEnrichOnly(cards) && enrichOnlySeated == EnrichOnlyPeoplePerPack) setAside.Add(cards);
            else Seat(cards);
        }
        // People set aside fill only the seats the rest of the pool could not.
        foreach (var cards in setAside)
        {
            if (seated == people) break;
            Seat(cards);
        }

        return new DrawResult { Targets = drawn, Seed = seed, LegendaryDiscards = discards };
    }

    /// <summary>
    /// How many people a pack seats whose only gap card is Enrich person, before it prefers people holding a New
    /// parent, New spouse or New child card: an enrichment pays a few points, a new relative many more.
    /// </summary>
    public const int EnrichOnlyPeoplePerPack = 2;

    static bool IsEnrichOnly(List<SelectionTarget> cards) =>
        cards.All(c => c.CardType == CardType.EnrichPerson.Display());
}

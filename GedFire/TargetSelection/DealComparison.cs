using GedCore;
using GedCore.Points;
using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// The deal on the tree before and after a changeset, read by the same rules select_targets deals by: the Enrich
/// person gaps the changeset closed, the never-married answer it recorded, and the cards a study scope withholds.
/// Each model is built only when a card asks for it.
/// </summary>
public sealed class DealComparison : ICardDeal
{
    readonly Lazy<GedModel> _before;
    readonly Lazy<GedModel> _after;
    readonly StudyScope? _scope;
    readonly HonestRange _honestRange;

    public DealComparison(GedDocument before, GedDocument after, StudyScope? scope = null, GapRules? rules = null)
    {
        _before = new Lazy<GedModel>(() => ModelBuilder.Build(before));
        _after = new Lazy<GedModel>(() => ModelBuilder.Build(after));
        _scope = scope;
        _honestRange = (rules ?? GapRules.Default).HonestRange;
    }

    public IReadOnlyList<EnrichGap> ClosedEnrichGaps(string xref)
    {
        if (!_before.Value.Individuals.TryGetValue(xref, out var was) || !_after.Value.Individuals.TryGetValue(xref, out var now))
            return [];

        var stillListed = EnrichRule.Gaps(now, _honestRange).ToHashSet();
        // A marriage the person is no longer a partner in was not enriched; its gaps left with it.
        var families = now.FamSpouse.Select(f => f.Xref).ToHashSet(StringComparer.Ordinal);
        return [.. EnrichRule.Gaps(was, _honestRange)
            .Where(gap => !stillListed.Contains(gap) && (gap.Record == xref || families.Contains(gap.Record)))];
    }

    public bool RecordedNeverMarried(string xref) =>
        _after.Value.Individuals.TryGetValue(xref, out var now) && NeverMarriedAnswer.IsRecorded(now)
        && !(_before.Value.Individuals.TryGetValue(xref, out var was) && NeverMarriedAnswer.IsRecorded(was));

    // The scope is judged on the tree the card was dealt from; a person or family the changeset creates held no card.
    public string? WithheldReason(PointsCard card, string? family)
    {
        if (_scope is null || CardTypeExtensions.Parse(card.Class) is not { } cardType) return null;
        if (!_before.Value.Individuals.TryGetValue(card.Xref, out var person)) return null;
        return _scope.WithheldReason(person, cardType, family is null ? null : _before.Value.Families.GetValueOrDefault(family));
    }
}

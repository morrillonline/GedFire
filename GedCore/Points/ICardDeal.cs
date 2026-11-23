namespace GedCore.Points;

/// <summary>
/// One part of a fact the deal lists as missing on an Enrich person card: the "date", "place" or "citation"
/// of a person's BIRT or DEAT, or of a family's MARR.
/// </summary>
public sealed record EnrichGap(string Tag, string Record, string Part);

/// <summary>
/// What the deal says of the tree before and after a changeset, so the points preview pays a card
/// for exactly what the deal priced it at and never closes a card the deal withholds.
/// </summary>
public interface ICardDeal
{
    /// <summary>The Enrich person gaps the deal listed for the person before the changeset and no longer lists after it.</summary>
    IReadOnlyList<EnrichGap> ClosedEnrichGaps(string xref);

    /// <summary>Whether the changeset recorded a proven never-married answer the person did not have before.</summary>
    bool RecordedNeverMarried(string xref);

    /// <summary>Why the deal withholds the card, for a New child card of <paramref name="family"/>; null when it deals it.</summary>
    string? WithheldReason(PointsCard card, string? family);
}

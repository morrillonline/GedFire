using GedCore.Points;
using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>One birth, death or marriage the Enrich person card lists, on the record that holds it.</summary>
public sealed record ListedFact(string Tag, string Record, EnrichmentEntry Entry)
{
    /// <summary>One gap per missing part: each is a point of the card.</summary>
    public IEnumerable<EnrichGap> Gaps
    {
        get
        {
            if (Entry.MissingDate) yield return new EnrichGap(Tag, Record, "date");
            if (Entry.MissingPlace) yield return new EnrichGap(Tag, Record, "place");
            if (Entry.MissingCitation) yield return new EnrichGap(Tag, Record, "citation");
        }
    }
}

/// <summary>
/// The Enrich person rule, the one place it is written: the deal prices a card by it and the points preview pays by
/// it. A birth, a death and each marriage is listed while its date or place is not exact or it has no citation; each
/// missing part is a point. An honest cited range is not a missing date, and an exact burial stands for the death.
/// </summary>
public static class EnrichRule
{
    /// <summary>The facts the card lists for <paramref name="person"/>, the marriage of each spouse family <paramref name="listsMarriageOf"/> accepts among them.</summary>
    public static List<ListedFact> ListedFacts(GedIndividual person, HonestRange honestRange, Func<GedFamily, bool>? listsMarriageOf = null)
    {
        var facts = new List<ListedFact>();
        AddIfImprovable(facts, "Birth", "BIRT", person.Xref, person.Birth, honestRange);
        if (!BurialSatisfiesDeath(person))
            AddIfImprovable(facts, "Death", "DEAT", person.Xref, person.Death, honestRange);
        // A family with no MARR at all is listed too: its date, place and citation are all missing.
        foreach (var family in person.FamSpouse.Where(f => listsMarriageOf?.Invoke(f) ?? true))
            AddIfImprovable(facts, "Marriage", "MARR", family.Xref, family.Marriage, honestRange);
        return facts;
    }

    /// <summary>Every gap the card lists for <paramref name="person"/>, across all of the person's marriages.</summary>
    public static IEnumerable<EnrichGap> Gaps(GedIndividual person, HonestRange honestRange) =>
        ListedFacts(person, honestRange).SelectMany(f => f.Gaps);

    /// <summary>A personal-note pseudo-source and a citation of no source record no evidence, so neither cites the fact.</summary>
    public static bool IsCited(GedEvent? gedEvent) => gedEvent is not null && gedEvent.Sources.Any(s => !s.NoCitation);

    // An exact burial closes the death gap: it dates the death closely enough for the study's purposes.
    static bool BurialSatisfiesDeath(GedIndividual person) =>
        person.Burial is { } burial && ExactnessRules.IsExactDate(burial.Date) && ExactnessRules.IsExactPlace(burial.Place);

    static void AddIfImprovable(List<ListedFact> facts, string fact, string tag, string record, GedEvent? gedEvent, HonestRange honestRange)
    {
        string? date = gedEvent?.Date;
        string? place = gedEvent?.Place;
        bool missingDate = !ExactnessRules.IsExactDate(date) && !(gedEvent is not null && honestRange.Closes(gedEvent));
        bool missingPlace = !ExactnessRules.IsExactPlace(place);
        bool missingCitation = !IsCited(gedEvent);
        if (!missingDate && !missingPlace && !missingCitation) return;

        facts.Add(new ListedFact(tag, record, new EnrichmentEntry
        {
            Fact = fact,
            CurrentDate = string.IsNullOrEmpty(date) ? null : date,
            CurrentPlace = string.IsNullOrEmpty(place) ? null : place,
            MissingDate = missingDate,
            MissingPlace = missingPlace,
            MissingCitation = missingCitation,
        }));
    }
}

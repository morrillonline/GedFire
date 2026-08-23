using GedCore;
using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// Detects every candidate gap in a GedModel and scores it (card type,
/// nominal points, difficulty). Does not filter by count or apply the
/// Legendary cap — TargetDrawer does that once the full candidate set is
/// known.
/// </summary>
public static class GapDetector
{
    /// <summary>
    /// No person born within this many years of today is ever surfaced as a
    /// target, regardless of card type — a hard privacy floor with no CLI
    /// override, because this design has no other way to keep a plausibly-
    /// living person out of a file meant to be handed to a research
    /// assistant. Unknown birth years are not gated: in practice a missing
    /// birth date means an under-documented old ancestor far more often
    /// than a deliberately-redacted living one, matching this design's
    /// existing lenient treatment of unknown dates/places elsewhere.
    /// </summary>
    const int PrivacyFloorYears = 100;

    /// <summary>
    /// Restrict to persons bearing one of <paramref name="surnames"/>, or
    /// married to one of them, then run every gap rule against each — except
    /// New parent, which only ever runs for an actual surname-bearer (see
    /// the check inline below).
    /// </summary>
    public static List<SelectionTarget> Detect(GedModel model, IEnumerable<string> surnames) =>
        Detect(model, surnames, DateTime.UtcNow.Year);

    /// <summary>Overload taking the current year explicitly, so the privacy floor is testable without a clock.</summary>
    public static List<SelectionTarget> Detect(GedModel model, IEnumerable<string> surnames, int currentYear)
    {
        var surnameSet = new HashSet<string>(
            surnames.Select(s => s.Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        int privacyFloorYear = currentYear - PrivacyFloorYears;

        var targets = new List<SelectionTarget>();

        // Family-scoped gaps (New child, Marriage enrichment) are detected
        // once per family, not once per qualifying spouse — a person and
        // their spouse can both pass the surname filter and both iterate
        // the same FAMS family, so this dedupes to whichever spouse the
        // model happens to visit first.
        var newChildFamiliesSeen = new HashSet<string>();
        var marriageEnrichFamiliesSeen = new HashSet<string>();

        foreach (var person in model.Individuals.Values)
        {
            if (!MatchesSurname(person, surnameSet)) continue;
            if (IsWithinPrivacyFloor(person, privacyFloorYear)) continue;

            // New parent means "go find this person's own parents" — for
            // someone who only qualifies by marrying into the surname (not
            // by carrying it), that would push research up into a wholly
            // unrelated family line. Restrict it to actual surname-bearers;
            // every other card type still applies to a married-in spouse.
            if (BearsSurname(person, surnameSet))
                DetectNewParent(person, targets);
            DetectNewSpouse(person, targets);
            DetectNewChild(person, newChildFamiliesSeen, targets);
            DetectEnrichBirth(person, targets);
            DetectEnrichDeath(person, targets);
            DetectEnrichMarriages(person, marriageEnrichFamiliesSeen, targets);
        }

        return targets;
    }

    static bool IsWithinPrivacyFloor(GedIndividual person, int privacyFloorYear)
    {
        int birthYear = GedDate.ParseYear(person.Birth?.Date);
        return birthYear > 0 && birthYear >= privacyFloorYear;
    }

    static bool BearsSurname(GedIndividual person, HashSet<string> surnames) =>
        surnames.Contains(person.LastNameRaw);

    static bool MatchesSurname(GedIndividual person, HashSet<string> surnames)
    {
        if (BearsSurname(person, surnames)) return true;
        foreach (var fam in person.FamSpouse)
        {
            var spouse = fam.SpouseOf(person);
            if (spouse != null && surnames.Contains(spouse.LastNameRaw)) return true;
        }
        return false;
    }

    // -------------------------------------------------------------------
    // New parent — person has zero or one recorded parents.
    // -------------------------------------------------------------------

    static void DetectNewParent(GedIndividual person, List<SelectionTarget> targets)
    {
        var fam = person.FamChild;
        int parentCount = (fam?.Husband != null ? 1 : 0) + (fam?.Wife != null ? 1 : 0);
        if (parentCount > 1) return;

        var difficulty = DifficultyScore.Compute(
            EraWeightForDate(person.Birth?.Date),
            ExactnessRules.GeographyWeight(person.Birth?.Place),
            BirthIsExact(person) ? 0 : 1);

        KnownParentEntry? known = null;
        if (parentCount == 1)
        {
            var knownParent = fam!.Husband ?? fam.Wife!;
            known = new KnownParentEntry { Xref = knownParent.Xref, Name = DisplayName(knownParent) };
        }

        targets.Add(BuildTarget(person, CardType.NewParent, nominalPoints: 10, difficulty, knownParent: known));
    }

    // -------------------------------------------------------------------
    // New spouse — no recorded spouse family, and did not die young.
    // -------------------------------------------------------------------

    static void DetectNewSpouse(GedIndividual person, List<SelectionTarget> targets)
    {
        if (person.FamSpouse.Count > 0) return;
        if (DiedYoung(person)) return;

        var difficulty = DifficultyScore.Compute(
            EraWeightForDate(person.Birth?.Date),
            ExactnessRules.GeographyWeight(person.Birth?.Place),
            BirthIsExact(person) ? 0 : 1);

        targets.Add(BuildTarget(person, CardType.NewSpouse, nominalPoints: 5, difficulty));
    }

    // Age at death from year subtraction only (day/month ignored) — coarse,
    // consistent with the GED-only, year-granularity signals used elsewhere
    // in this design. Unknown birth or death year never counts as "young".
    static bool DiedYoung(GedIndividual person)
    {
        if (person.Death is null) return false;
        int birthYear = GedDate.ParseYear(person.Birth?.Date);
        int deathYear = GedDate.ParseYear(person.Death.Date);
        if (birthYear <= 0 || deathYear <= 0) return false;
        return deathYear - birthYear < 17;
    }

    // -------------------------------------------------------------------
    // New child — a spouse family (FAMS) with no children recorded in it.
    // -------------------------------------------------------------------

    static void DetectNewChild(GedIndividual person, HashSet<string> familiesSeen, List<SelectionTarget> targets)
    {
        foreach (var fam in person.FamSpouse)
        {
            if (fam.Children.Count > 0) continue;
            if (!familiesSeen.Add(fam.Xref)) continue;

            // Era/geography key off the marriage when one is recorded (this
            // is a descendancy target), else fall back to the subject's own
            // birth — mirroring the design's "birth/marriage" wording for
            // both signals.
            bool hasMarriageYear = fam.Marriage != null && GedDate.ParseYear(fam.Marriage.Date) > 0;
            int era = hasMarriageYear
                ? ExactnessRules.EraWeight(GedDate.ParseYear(fam.Marriage!.Date))
                : EraWeightForDate(person.Birth?.Date);
            string? geoPlace = fam.Marriage is { Place.Length: > 0 } m ? m.Place : person.Birth?.Place;

            var difficulty = DifficultyScore.Compute(
                era,
                ExactnessRules.GeographyWeight(geoPlace),
                BirthIsExact(person) ? 0 : 1);

            var spouse = fam.SpouseOf(person);
            targets.Add(BuildTarget(person, CardType.NewChild, nominalPoints: 5, difficulty,
                spouseFamily: new SpouseFamilyEntry
                {
                    FamilyXref = fam.Xref,
                    SpouseXref = spouse?.Xref,
                    SpouseName = spouse != null ? DisplayName(spouse) : null,
                }));
        }
    }

    // -------------------------------------------------------------------
    // Enrich person — birth/marriage/death date or place not exact.
    // Geography always keys off the subject's own birth place, even when
    // enriching a different fact, per the design's geography-weight table.
    // -------------------------------------------------------------------

    static void DetectEnrichBirth(GedIndividual person, List<SelectionTarget> targets)
    {
        AddEnrichmentIfInexact(person, "Birth", person.Birth?.Date, person.Birth?.Place, targets);
    }

    static void DetectEnrichDeath(GedIndividual person, List<SelectionTarget> targets)
    {
        AddEnrichmentIfInexact(person, "Death", person.Death?.Date, person.Death?.Place, targets);
    }

    static void DetectEnrichMarriages(GedIndividual person, HashSet<string> familiesSeen, List<SelectionTarget> targets)
    {
        foreach (var fam in person.FamSpouse)
        {
            if (!familiesSeen.Add(fam.Xref)) continue;
            // A missing MARR record (fam.Marriage null) still counts as "not
            // exact" — both date and place read as absent, per the design's
            // "the whole fact being absent" rule.
            AddEnrichmentIfInexact(person, "Marriage", fam.Marriage?.Date, fam.Marriage?.Place, targets);
        }
    }

    static void AddEnrichmentIfInexact(GedIndividual person, string fact, string? date, string? place, List<SelectionTarget> targets)
    {
        bool missingDate = !ExactnessRules.IsExactDate(date);
        bool missingPlace = !ExactnessRules.IsExactPlace(place);
        if (!missingDate && !missingPlace) return;

        int nominal = (missingDate ? 1 : 0) + (missingPlace ? 1 : 0);
        var difficulty = DifficultyScore.Compute(
            EraWeightForDate(date),
            ExactnessRules.GeographyWeight(person.Birth?.Place),
            contextAdjustment: 0); // anchor strength does not apply to Enrich person

        targets.Add(BuildTarget(person, CardType.EnrichPerson, nominal, difficulty,
            enrichment: new EnrichmentEntry
            {
                Fact = fact,
                CurrentDate = NullIfEmpty(date),
                CurrentPlace = NullIfEmpty(place),
                MissingDate = missingDate,
                MissingPlace = missingPlace,
            }));
    }

    // -------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------

    static bool BirthIsExact(GedIndividual person) =>
        ExactnessRules.IsExactDate(person.Birth?.Date) && ExactnessRules.IsExactPlace(person.Birth?.Place);

    static int EraWeightForDate(string? date)
    {
        int year = GedDate.ParseYear(date);
        return ExactnessRules.EraWeight(year > 0 ? year : null);
    }

    static SelectionTarget BuildTarget(
        GedIndividual person, CardType cardType, int nominalPoints, DifficultyScore difficulty,
        KnownParentEntry? knownParent = null, SpouseFamilyEntry? spouseFamily = null, EnrichmentEntry? enrichment = null) =>
        new()
        {
            Xref = person.Xref,
            Name = DisplayName(person),
            Surname = person.LastName,
            Born = FormatDisplay(person.Birth),
            BirthPlace = NullIfEmpty(person.Birth?.Place),
            Died = FormatDisplay(person.Death),
            CardType = cardType.Display(),
            NominalPoints = nominalPoints,
            Difficulty = new DifficultyEntry
            {
                Band = difficulty.Band,
                EraWeight = difficulty.EraWeight,
                GeoWeight = difficulty.GeoWeight,
                ContextAdjustment = difficulty.ContextAdjustment,
            },
            Score = nominalPoints + difficulty.Bonus,
            KnownParent = knownParent,
            SpouseFamily = spouseFamily,
            Enrichment = enrichment,
        };

    static string DisplayName(GedIndividual p) => (p.FirstMiddle() + " " + p.LastName).Trim();

    static string? FormatDisplay(GedEvent? ev)
    {
        if (ev is null) return null;
        string date = ev.Date;
        string place = ev.Place;
        if (date.Length == 0 && place.Length == 0) return null;
        if (date.Length == 0) return place;
        if (place.Length == 0) return date;
        return $"{date}, {place}";
    }

    static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

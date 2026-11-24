using System.Text.Json.Serialization;
using GedCore;
using GedFire.Gen;

namespace GedFire.TargetSelection;

public sealed record SuppressedCard(string Xref, string Name, string CardType, string Reason)
{
    /// <summary>The study scope withheld the card; such a card cannot be forced, and a draw reports it only for the people drawn.</summary>
    [JsonIgnore]
    public bool OutOfScope { get; init; }
}

/// <summary>
/// Detects every candidate gap in a GedModel and scores it (card type,
/// nominal points, difficulty). Does not filter by count or apply the
/// Legendary cap — TargetDrawer does that once the full candidate set is
/// known.
/// </summary>
public static class GapDetector
{
    /// <summary>
    /// No person born within this many years of today is surfaced by a
    /// surname draw, regardless of card type — a privacy floor with no CLI
    /// override. A person the owner names is scored whenever born. Unknown
    /// birth years are not gated because a missing birth
    /// date is treated consistently with other unknown dates and places.
    /// </summary>
    const int PrivacyFloorYears = 100;

    /// <summary>
    /// Restrict to persons bearing one of <paramref name="surnames"/>, or
    /// married to one of them, then run every gap rule against each. With no
    /// study scope in <paramref name="rules"/>, New parent runs only for an
    /// actual surname-bearer and New child only for a family whose husband is
    /// one, and neither is reported; a study scope withholds those cards
    /// itself and reports each.
    /// </summary>
    public static List<SelectionTarget> Detect(GedModel model, IEnumerable<string> surnames, ICollection<SuppressedCard>? suppressedCards = null, GapRules? rules = null) =>
        Detect(model, surnames, DateTime.UtcNow.Year, suppressedCards, rules);

    /// <summary>Overload taking the current year explicitly, so the privacy floor is testable without a clock.</summary>
    public static List<SelectionTarget> Detect(
        GedModel model, IEnumerable<string> surnames, int currentYear, ICollection<SuppressedCard>? suppressedCards = null, GapRules? rules = null)
    {
        rules ??= GapRules.Default;
        var withholding = new Withholding(suppressedCards, rules);
        var drawn = new StudyScope(surnames);
        bool scoped = rules.Scope is not null;
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
            if (!drawn.BearsOrMarriedOne(person)) continue;
            if (IsWithinPrivacyFloor(person, privacyFloorYear)) continue;

            // New parent means "go find this person's own parents" — for
            // someone who only qualifies by marrying into the surname (not
            // by carrying it), that would push research up into a wholly
            // unrelated family line.
            if (scoped || drawn.Bears(person))
                DetectNewParent(person, targets, withholding);
            DetectNewSpouse(person, targets, withholding);
            DetectNewChild(person, fam => scoped || drawn.HusbandBears(fam), newChildFamiliesSeen, targets, withholding, rules.NewChild);
            DetectEnrichPerson(person, marriageEnrichFamiliesSeen, targets, rules.HonestRange, withholding);
        }

        return targets;
    }

    /// <summary>
    /// Score exactly <paramref name="people"/>, in order, with no surname
    /// filter: the caller has already nominated each person, so every gap
    /// rule runs for each of them. The privacy floor does not apply: it keeps
    /// the living out of a random draw, not out of a person the owner names.
    /// </summary>
    public static List<SelectionTarget> DetectForPeople(
        IEnumerable<GedIndividual> people, ICollection<SuppressedCard>? suppressedCards = null, GapRules? rules = null)
    {
        rules ??= GapRules.Default;
        var withholding = new Withholding(suppressedCards, rules);
        var targets = new List<SelectionTarget>();
        var newChildFamiliesSeen = new HashSet<string>();
        var marriageEnrichFamiliesSeen = new HashSet<string>();

        foreach (var person in people)
        {
            DetectNewParent(person, targets, withholding);
            DetectNewSpouse(person, targets, withholding);
            DetectNewChild(person, _ => true, newChildFamiliesSeen, targets, withholding, rules.NewChild);
            DetectEnrichPerson(person, marriageEnrichFamiliesSeen, targets, rules.HonestRange, withholding);
        }

        return targets;
    }

    static bool IsWithinPrivacyFloor(GedIndividual person, int privacyFloorYear)
    {
        int birthYear = GedDate.ParseYear(person.Birth?.Date);
        return birthYear > 0 && birthYear >= privacyFloorYear;
    }

    // -------------------------------------------------------------------
    // Biography — not a gap: one repeatable card per man for his cited
    // biography beyond birth, death and marriage (facts and prose), open even
    // when it exists. A drawn man gets it
    // only beside another of his cards; a man the owner names gets it alone,
    // whenever he was born.
    // Priced like enrichment: era and region of the person's own birth, no
    // anchor-strength adjustment.
    // -------------------------------------------------------------------

    public const int BiographyPoints = 3;

    /// <summary>One Biography card for each man among <paramref name="people"/> outside the privacy floor, in order.</summary>
    public static List<SelectionTarget> BiographyCards(IEnumerable<GedIndividual> people) =>
        BiographyCards(people, DateTime.UtcNow.Year);

    /// <summary>
    /// Every card that pays for each of <paramref name="people"/>, in order: the person's <paramref name="gapCards"/>,
    /// then, for a man with at least one of them outside the privacy floor, his Biography card.
    /// </summary>
    public static List<SelectionTarget> WithBiographyCards(
        IEnumerable<GedIndividual> people, IReadOnlyList<SelectionTarget> gapCards,
        ICollection<SuppressedCard>? suppressedCards = null, GapRules? rules = null) =>
        WithBiographyCards(people, gapCards, named: false, new Withholding(suppressedCards, rules ?? GapRules.Default));

    /// <summary>
    /// Every card that pays for each of the named <paramref name="people"/>, in order: the person's <paramref name="gapCards"/>,
    /// then, for a man, his Biography card, even when he has no other card and whenever he was born.
    /// </summary>
    public static List<SelectionTarget> WithNamedBiographyCards(
        IEnumerable<GedIndividual> people, IReadOnlyList<SelectionTarget> gapCards,
        ICollection<SuppressedCard>? suppressedCards = null, GapRules? rules = null) =>
        WithBiographyCards(people, gapCards, named: true, new Withholding(suppressedCards, rules ?? GapRules.Default));

    static List<SelectionTarget> WithBiographyCards(
        IEnumerable<GedIndividual> people, IReadOnlyList<SelectionTarget> gapCards, bool named, Withholding withholding)
    {
        var byPerson = gapCards.ToLookup(c => c.Xref, StringComparer.Ordinal);
        int? privacyFloorYear = named ? null : DateTime.UtcNow.Year - PrivacyFloorYears;
        return [.. people.SelectMany(person => named || byPerson[person.Xref].Any()
            ? byPerson[person.Xref].Concat(BiographyCards([person], privacyFloorYear, withholding))
            : [])];
    }

    /// <summary>Overload taking the current year explicitly, so the privacy floor is testable without a clock.</summary>
    public static List<SelectionTarget> BiographyCards(IEnumerable<GedIndividual> people, int currentYear) =>
        BiographyCards(people, currentYear - PrivacyFloorYears, new Withholding(null, GapRules.Default));

    // A null privacyFloorYear deals a card to a man whenever he was born.
    static List<SelectionTarget> BiographyCards(IEnumerable<GedIndividual> people, int? privacyFloorYear, Withholding withholding) =>
        [.. people
            .Where(person => person.IsMale
                && !(privacyFloorYear is { } floor && IsWithinPrivacyFloor(person, floor))
                && !withholding.OutOfScope(person, CardType.Biography))
            .Select(person => BuildTarget(person, CardType.Biography, BiographyPoints, DifficultyScore.Compute(
                EraWeightForDate(BiographyEraDate(person)), ExactnessRules.GeographyWeight(person.Birth?.Place), contextAdjustment: 0)))];

    // The person's birth, else the first of death and marriage that is dated, as enrichment falls back.
    static string? BiographyEraDate(GedIndividual person) =>
        new[] { person.Birth?.Date, person.Death?.Date }
            .Concat(person.FamSpouse.Select(f => f.Marriage?.Date))
            .FirstOrDefault(d => !string.IsNullOrEmpty(d));

    static bool HasAnchor(GedEvent? gedEvent) =>
        gedEvent is not null && (ExactnessRules.IsExactDate(gedEvent.Date) || !string.IsNullOrWhiteSpace(gedEvent.Place));

    static bool IsBareBones(GedIndividual? person) =>
        person is null || (!HasAnchor(person.Birth) && !HasAnchor(person.Death) &&
            !person.OtherEvents.Any(gedEvent => gedEvent.Tag is "CHR" or "BAPM" or "BURI" && HasAnchor(gedEvent)));

    static bool SuppressBareSubject(GedIndividual person, CardType cardType, Withholding withholding) =>
        IsBareBones(person) && withholding.Withhold(person, cardType, "bare-bones subject \u2014 enrich first");

    /// <summary>Reports each card a rule withholds. The owner can force a card past every rule but the study scope.</summary>
    sealed class Withholding(ICollection<SuppressedCard>? reported, GapRules rules)
    {
        /// <summary>True when the card is withheld, so the caller skips it.</summary>
        public bool Withhold(GedIndividual person, CardType cardType, string reason)
        {
            if (rules.IncludeSuppressed) return false;
            reported?.Add(new SuppressedCard(person.Xref, DisplayName(person), cardType.Display(), reason));
            return true;
        }

        /// <summary>True when the study scope withholds the card, of <paramref name="family"/> for a New child card.</summary>
        public bool OutOfScope(GedIndividual person, CardType cardType, GedFamily? family = null)
        {
            if (ScopeReason(person, cardType, family) is not { } reason) return false;
            ReportOutOfScope(person, cardType, reason);
            return true;
        }

        public string? ScopeReason(GedIndividual person, CardType cardType, GedFamily? family = null) =>
            rules.Scope?.WithheldReason(person, cardType, family);

        public void ReportOutOfScope(GedIndividual person, CardType cardType, string reason) =>
            reported?.Add(new SuppressedCard(person.Xref, DisplayName(person), cardType.Display(), reason) { OutOfScope = true });
    }

    // -------------------------------------------------------------------
    // New parent — person has zero or one recorded parents.
    // -------------------------------------------------------------------

    static void DetectNewParent(GedIndividual person, List<SelectionTarget> targets, Withholding withholding)
    {
        var fam = person.FamChild;
        int parentCount = (fam?.Husband != null ? 1 : 0) + (fam?.Wife != null ? 1 : 0);
        if (parentCount > 1) return;
        if (withholding.OutOfScope(person, CardType.NewParent)) return;
        if (SuppressBareSubject(person, CardType.NewParent, withholding)) return;

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
    // New spouse — no recorded spouse family, did not die young, and no
    // proven never-married answer.
    // -------------------------------------------------------------------

    static void DetectNewSpouse(GedIndividual person, List<SelectionTarget> targets, Withholding withholding)
    {
        if (person.FamSpouse.Count > 0) return;
        if (DiedYoung(person)) return;
        if (withholding.OutOfScope(person, CardType.NewSpouse)) return;
        if (NeverMarriedAnswer.IsRecorded(person) && withholding.Withhold(person, CardType.NewSpouse, NeverMarriedAnswer.Reason)) return;
        if (SuppressBareSubject(person, CardType.NewSpouse, withholding)) return;

        var difficulty = DifficultyScore.Compute(
            EraWeightForDate(person.Birth?.Date),
            ExactnessRules.GeographyWeight(person.Birth?.Place),
            BirthIsExact(person) ? 0 : 1);

        targets.Add(BuildTarget(person, CardType.NewSpouse, nominalPoints: 5, difficulty));
    }

    // Age at death from year subtraction only (day/month ignored) — coarse,
    // consistent with the GED-only, year-granularity signals used elsewhere.
    // Unknown birth or death year never counts as "young".
    static bool DiedYoung(GedIndividual person)
    {
        if (person.DeathOrBurial is not { } death) return false;
        int birthYear = GedDate.ParseYear(person.Birth?.Date);
        int deathYear = GedDate.ParseYear(death.Date);
        if (birthYear <= 0 || deathYear <= 0) return false;
        return deathYear - birthYear < 17;
    }

    // -------------------------------------------------------------------
    // New child — a spouse family (FAMS) with no children recorded in it.
    // In a surname study the family only qualifies when its husband bears
    // a study surname (children of any other father will not carry it);
    // for explicitly nominated people every childless family qualifies.
    // -------------------------------------------------------------------

    static void DetectNewChild(
        GedIndividual person, Func<GedFamily, bool> familyQualifies, HashSet<string> familiesSeen, List<SelectionTarget> targets,
        Withholding withholding, NewChildLimits limits)
    {
        foreach (var fam in person.FamSpouse)
        {
            if (fam.Children.Count > 0) continue;
            if (!familyQualifies(fam)) continue;
            if (!familiesSeen.Add(fam.Xref)) continue;
            if (withholding.OutOfScope(person, CardType.NewChild, fam)) continue;
            if (NewChildWithheldReason(fam, limits) is { } reason && withholding.Withhold(person, CardType.NewChild, reason)) continue;

            // Era/geography key off the marriage when one is recorded (this
            // is a descendancy target), else fall back to the subject's own
            // birth, keeping both signals tied to the descendancy event.
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

    // The first rule that rules out this couple's New child card, or null when the card is open.
    static string? NewChildWithheldReason(GedFamily fam, NewChildLimits limits)
    {
        if (IsBareBones(fam.Husband) && IsBareBones(fam.Wife) && !HasAnchor(fam.Marriage))
            return "bare-bones family (both spouses and marriage lack anchors) \u2014 enrich first";
        if (EarlierMarriageChildren.AllPredate(fam)) return EarlierMarriageChildren.Reason;
        return ChildlessCouple.WithholdReason(fam, limits);
    }

    // -------------------------------------------------------------------
    // Enrich person — one card per person listing what EnrichRule lists.
    // Each missing part is a point; the difficulty bonus is added once.
    // Geography keys off the subject's own birth place.
    // -------------------------------------------------------------------

    static void DetectEnrichPerson(
        GedIndividual person, HashSet<string> familiesSeen, List<SelectionTarget> targets, HonestRange honestRange, Withholding withholding)
    {
        // Judged before the marriages are claimed, so a person outside the scope takes no family's marriage from a spouse inside it.
        if (withholding.ScopeReason(person, CardType.EnrichPerson) is { } outOfScope)
        {
            if (EnrichRule.ListedFacts(person, honestRange).Count > 0) withholding.ReportOutOfScope(person, CardType.EnrichPerson, outOfScope);
            return;
        }

        var facts = EnrichRule.ListedFacts(person, honestRange, family => familiesSeen.Add(family.Xref)).Select(f => f.Entry).ToList();
        if (facts.Count == 0) return;

        int nominal = facts.Sum(f => (f.MissingDate ? 1 : 0) + (f.MissingPlace ? 1 : 0) + (f.MissingCitation ? 1 : 0));
        var difficulty = DifficultyScore.Compute(
            EraWeightForDate(EarliestDate(person, facts)),
            ExactnessRules.GeographyWeight(person.Birth?.Place),
            contextAdjustment: 0); // anchor strength does not apply to Enrich person

        targets.Add(BuildTarget(person, CardType.EnrichPerson, nominal, difficulty, enrichments: facts));
    }

    // The era of the person's own birth, else of the first listed fact that has a date.
    static string? EarliestDate(GedIndividual person, List<EnrichmentEntry> facts) =>
        NullIfEmpty(person.Birth?.Date) ?? facts.Select(f => f.CurrentDate).FirstOrDefault(d => d is not null);

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
        KnownParentEntry? knownParent = null, SpouseFamilyEntry? spouseFamily = null, IReadOnlyList<EnrichmentEntry>? enrichments = null) =>
        new()
        {
            Xref = person.Xref,
            Name = DisplayName(person),
            Surname = person.LastName,
            Born = FormatDisplay(person.Birth),
            Died = FormatDisplay(person.DeathOrBurial),
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
            Enrichments = enrichments,
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

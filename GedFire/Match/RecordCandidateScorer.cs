using GedCore;
using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Scores one recalled tree candidate against one record person: hard
// exclusions first (dead before the event, wrong generation, wrong sex),
// then four subscores — name, lifespan, kin overlap, place — each 0-100,
// combined over whichever dimensions the record actually supplies.
// Deterministic throughout.
// ---------------------------------------------------------------------------

public sealed record RecordCandidateScore(
    GedIndividual Individual,
    double Composite,
    double NameScore,
    double? LifespanScore,
    double? KinOverlapScore,
    double? PlaceScore,
    IReadOnlyList<RecordKinMatch> MatchedKin);

public sealed class RecordCandidateScorer
{
    // The name and the record's stated kin carry equal, dominant weight;
    // lifespan and place corroborate. Normalized over available dimensions.
    const double NameWeight = 35.0;
    const double KinWeight = 35.0;
    const double LifespanWeight = 15.0;
    const double PlaceWeight = 15.0;

    // Below this Jaro-Winkler similarity a kin name is a different person, not a variant spelling.
    const double KinNameFloor = 0.85;
    // The matcher's nickname credit, GivenNicknameFixedPoints / GivenWeight.
    const double NicknameSimilarity = 0.96;
    const int KinBirthYearTolerance = 2;

    // Beyond this birth-year gap the candidate is another generation, not a mis-aged match.
    public const int BirthYearExclusionGap = 10;
    public const int MaxAgeAtEvent = 120;

    readonly NicknameDirectory _nicknames;

    public RecordCandidateScorer(NicknameDirectory nicknames) =>
        _nicknames = nicknames ?? throw new ArgumentNullException(nameof(nicknames));

    /// <summary>The reason this candidate cannot be the record person at all, or null when it remains possible.</summary>
    public static string? ExclusionReason(GedIndividual candidate, RecordPersonDescription person, int? eventYear)
    {
        int born = GedDate.ParseYear(candidate.Birth?.Date);
        int died = GedDate.ParseYear(candidate.DeathOrBurial?.Date);

        if (person.IsMale is bool stated && candidate.SexRecorded && candidate.IsMale != stated)
            return $"recorded as {(candidate.IsMale ? "male" : "female")}, the record says {(stated ? "male" : "female")}";
        if (eventYear is int ev)
        {
            if (died > 0 && died < ev) return $"died in {died}, before the record's {ev} event";
            if (born > 0 && born > ev) return $"born in {born}, after the record's {ev} event";
            if (born > 0 && ev - born > MaxAgeAtEvent) return $"would be {ev - born} at the record's {ev} event";
        }
        if (person.BirthYear is int statedYear && born > 0 && Math.Abs(born - statedYear) > BirthYearExclusionGap)
            return $"born in {born}, more than {BirthYearExclusionGap} years from the record's {statedYear}";
        return null;
    }

    public RecordCandidateScore Score(
        ScoredMatch match, RecordPersonDescription person, StatedKin kin, RecordEventDescription? recordEvent)
    {
        var indi = match.Individual;
        double? lifespan = LifespanScore(indi, person);
        var matchedKin = new List<RecordKinMatch>();
        double? kinOverlap = kin.Any ? KinOverlapScore(indi, kin, matchedKin) : null;
        double? place = PlaceScore(indi, person, recordEvent);

        double points = match.NameOnlyScore / 100.0 * NameWeight;
        double weight = NameWeight;
        if (lifespan is double l) { points += l / 100.0 * LifespanWeight; weight += LifespanWeight; }
        if (kinOverlap is double k) { points += k / 100.0 * KinWeight; weight += KinWeight; }
        if (place is double p) { points += p / 100.0 * PlaceWeight; weight += PlaceWeight; }

        return new RecordCandidateScore(
            indi,
            Math.Round(points * 100.0 / weight, 1),
            Math.Round(match.NameOnlyScore, 1),
            Round(lifespan),
            Round(kinOverlap),
            Round(place),
            matchedKin);
    }

    static double? Round(double? value) => value is double d ? Math.Round(d, 1) : null;

    // Same decay as the matcher's year hints: 0 years apart is full agreement, 3+ is none.
    static double? LifespanScore(GedIndividual indi, RecordPersonDescription person)
    {
        int born = GedDate.ParseYear(indi.Birth?.Date);
        if (person.BirthYear is not int stated || born <= 0) return null;
        return Math.Abs(born - stated) switch { 0 => 100.0, 1 => 100.0 * 2 / 3, 2 => 100.0 / 3, _ => 0.0 };
    }

    double KinOverlapScore(GedIndividual indi, StatedKin kin, List<RecordKinMatch> matchedKin)
    {
        var categories = new (string Relation, IReadOnlyList<RecordPersonDescription> Stated, List<GedIndividual> Tree)[]
        {
            ("spouse", kin.Spouses, Spouses(indi)),
            ("parent", kin.Parents, Parents(indi)),
            ("child", kin.Children, Children(indi)),
            ("sibling", kin.Siblings, Siblings(indi)),
        };

        double total = 0;
        int stated = 0;
        foreach (var (relation, statedKin, treeKin) in categories)
        {
            foreach (var recordKin in statedKin)
            {
                stated++;
                if (BestKinMatch(recordKin, treeKin) is not var (treePerson, similarity)) continue;
                total += similarity * 100.0;
                matchedKin.Add(new RecordKinMatch(relation, recordKin.Name, treePerson.Xref, PersonDisplay.FullName(treePerson)));
            }
        }
        return stated == 0 ? 0 : total / stated;
    }

    (GedIndividual Person, double Similarity)? BestKinMatch(
        RecordPersonDescription recordKin, List<GedIndividual> treeKin)
    {
        (GedIndividual Person, double Similarity)? best = null;
        foreach (var tree in treeKin)
        {
            if (recordKin.BirthYear is int statedYear)
            {
                int treeBorn = GedDate.ParseYear(tree.Birth?.Date);
                if (treeBorn > 0 && Math.Abs(treeBorn - statedYear) > KinBirthYearTolerance) continue;
            }
            double similarity = NameSimilarity(recordKin.Name, tree);
            if (similarity < KinNameFloor) continue;
            if (best is null || similarity > best.Value.Similarity ||
                (similarity == best.Value.Similarity && string.CompareOrdinal(tree.Xref, best.Value.Person.Xref) < 0))
            {
                best = (tree, similarity);
            }
        }
        return best;
    }

    // A record often names kin by given name alone, so compare against both the
    // given and the full name and take the better, with nickname equivalence
    // standing in for similarity as the person matcher's given-name scoring does.
    double NameSimilarity(string recordName, GedIndividual tree)
    {
        string normalized = PersonNameNormalizer.Normalize(recordName);
        string treeGiven = PersonNameNormalizer.Normalize(tree.FirstMiddle());
        string treeFull = PersonNameNormalizer.Normalize(PersonDisplay.FullName(tree));
        double similarity = Math.Max(
            JaroWinkler.Similarity(normalized, treeGiven),
            JaroWinkler.Similarity(normalized, treeFull));
        bool? isMale = tree.SexRecorded ? tree.IsMale : null;
        if (_nicknames.AreEquivalent(FirstToken(normalized), FirstToken(treeGiven), isMale))
            similarity = Math.Max(similarity, NicknameSimilarity);
        return similarity;
    }

    static string FirstToken(string normalized)
    {
        int space = normalized.IndexOf(' ');
        return space < 0 ? normalized : normalized[..space];
    }

    static List<GedIndividual> Spouses(GedIndividual indi) =>
        [.. indi.FamSpouse.Select(f => f.SpouseOf(indi)).OfType<GedIndividual>()];

    static List<GedIndividual> Children(GedIndividual indi) =>
        [.. indi.FamSpouse.SelectMany(f => f.Children)];

    static List<GedIndividual> Parents(GedIndividual indi)
    {
        var parents = new List<GedIndividual>(2);
        if (indi.FamChild?.Husband is { } husband) parents.Add(husband);
        if (indi.FamChild?.Wife is { } wife) parents.Add(wife);
        return parents;
    }

    static List<GedIndividual> Siblings(GedIndividual indi) =>
        indi.FamChild is { } fam ? [.. fam.Children.Where(c => !ReferenceEquals(c, indi))] : [];

    static double? PlaceScore(GedIndividual indi, RecordPersonDescription person, RecordEventDescription? recordEvent)
    {
        var components = new List<double>(2);
        AddPlaceComponent(person.BirthPlace, [indi.Birth?.Place], components);
        AddPlaceComponent(recordEvent?.Place, AllPlaces(indi), components);
        return components.Count == 0 ? null : components.Average();
    }

    // Containment either way, like the person matcher's place hint: "Boone County, Iowa" agrees with "Iowa".
    static void AddPlaceComponent(string? recordPlace, IEnumerable<string?> candidatePlaces, List<double> components)
    {
        string hint = PersonNameNormalizer.Normalize(recordPlace);
        if (hint.Length == 0) return;
        var known = candidatePlaces.Select(PersonNameNormalizer.Normalize).Where(p => p.Length > 0).ToList();
        if (known.Count == 0) return;
        components.Add(known.Any(p =>
            p.Contains(hint, StringComparison.Ordinal) || hint.Contains(p, StringComparison.Ordinal)) ? 100.0 : 0.0);
    }

    static IEnumerable<string?> AllPlaces(GedIndividual indi)
    {
        foreach (var ev in indi.GetEvents()) yield return ev.Place;
        foreach (var fam in indi.FamSpouse) yield return fam.Marriage?.Place;
    }
}

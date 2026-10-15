using GedCore;
using GedCore.Validate;
using GedFire.Gen;
using GedFire.Match;
using GedFire.TargetSelection;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Composes one person's research dispatch packet from the pieces the other
// tools already compute: get_record's person detail, one-line kin summaries,
// select_targets' gap detection and duplicate pairing, plausibility findings
// filtered to the person, and the person's and their families' note texts.
// Reads the model only; no new scoring logic lives here.
// ---------------------------------------------------------------------------

public sealed class ResearchBriefBuilder
{
    static readonly string[] BirthBoundingTags = ["CHR", "BAPM"];

    readonly RecordMapper _mapper;

    public ResearchBriefBuilder(string mediaDirectory) => _mapper = new RecordMapper(mediaDirectory);

    public ResearchBrief Build(
        DocumentSnapshot snapshot, GedIndividual person, string? cardType,
        IReadOnlyList<GedDiagnostic> documentFindings, CancellationToken cancellationToken = default)
    {
        var duplicates = Duplicates(snapshot.MatchIndex, person.Xref, cancellationToken);

        return new ResearchBrief
        {
            Xref = person.Xref,
            CardType = cardType,
            Person = (PersonRecord)_mapper.Map(snapshot.Model, person.Xref),
            Kin = Kin(person),
            Gaps = GapDetector.DetectForPeople([person]),
            PossibleDuplicateOf = [.. duplicates.Take(DuplicateAnnotator.MaxListedDuplicates)],
            PossibleDuplicateCount = duplicates.Count,
            Plausibility = Plausibility(person, documentFindings),
            Notes = Notes(person),
            Emphasis = cardType is null ? null : Emphasis(person, cardType),
        };
    }

    static ResearchBriefKin Kin(GedIndividual person)
    {
        var parents = new List<KinSummary>(2);
        if (person.FamChild?.Husband is { } father) parents.Add(Summarize(father));
        if (person.FamChild?.Wife is { } mother) parents.Add(Summarize(mother));

        return new ResearchBriefKin(
            parents,
            [.. (person.FamChild?.Children ?? []).Where(c => !ReferenceEquals(c, person)).Select(Summarize)],
            [.. person.FamSpouse.Select(f => Summarize(f, person))],
            [.. person.FamSpouse.SelectMany(f => f.Children).Select(Summarize)]);
    }

    static SpouseSummary Summarize(GedFamily family, GedIndividual person)
    {
        var spouse = family.SpouseOf(person);
        return new SpouseSummary(
            family.Xref,
            spouse is null ? null : Summarize(spouse),
            NullIfEmpty(family.Marriage?.Date),
            NullIfEmpty(family.Marriage?.Place));
    }

    static KinSummary Summarize(GedIndividual kin) => new(
        kin.Xref,
        PersonDisplay.FullName(kin),
        FormatDisplay(kin.Birth),
        FormatDisplay(kin.DeathOrBurial));

    static List<PossibleDuplicateEntry> Duplicates(
        MatchIndex index, string xref, CancellationToken cancellationToken)
    {
        var pairs = PersonMatcher.FindPossibleDuplicates(
            index, new HashSet<string>([xref], StringComparer.Ordinal), cancellationToken);
        return [.. pairs
            .Select(p => new PossibleDuplicateEntry(p.FirstId == xref ? p.SecondId : p.FirstId, Math.Round(p.Score, 1)))
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Xref, StringComparer.Ordinal)];
    }

    // GEN301 pairs are already the duplicates section; repeating them here would say nothing new.
    static IReadOnlyList<ResearchBriefFinding> Plausibility(
        GedIndividual person, IReadOnlyList<GedDiagnostic> documentFindings)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal) { person.Xref };
        if (person.FamChild is { } famChild) anchors.Add(famChild.Xref);
        foreach (var fam in person.FamSpouse) anchors.Add(fam.Xref);

        return [.. documentFindings
            .Where(d => d.Code != "GEN301" && d.Xref != null && anchors.Contains(d.Xref))
            .Select(ResearchBriefFinding.From)];
    }

    static ResearchBriefNotes Notes(GedIndividual person)
    {
        var families = new List<FamilyNotes>();
        void Add(GedFamily? family)
        {
            if (family is null || family.NarrativeNotes.Count == 0) return;
            if (families.Any(f => f.Xref == family.Xref)) return;
            families.Add(new FamilyNotes(family.Xref, [.. family.NarrativeNotes.Select(n => n.Text)]));
        }

        Add(person.FamChild);
        foreach (var fam in person.FamSpouse) Add(fam);

        return new ResearchBriefNotes([.. person.NarrativeNotes.Select(n => n.Text)], families);
    }

    static ResearchBriefEmphasis Emphasis(GedIndividual person, string cardType) => new()
    {
        ChildrenWithUnknownOtherParent = cardType == "new-spouse"
            ? [.. person.FamSpouse.Where(f => f.SpouseOf(person) is null).SelectMany(f => f.Children).Select(Summarize)]
            : null,
        BirthBoundingFacts = cardType is "enrich-birth" or "enrich-person"
            ? BirthBounding(person)
            : null,
        DeathBoundingFacts = cardType is "enrich-death" or "enrich-person"
            ? DeathBounding(person)
            : null,
    };

    // Christenings, censuses, and the person's own marriages all put a floor or ceiling under the birth year.
    static IReadOnlyList<BriefFact> BirthBounding(GedIndividual person)
    {
        var facts = person.OtherEvents.Where(e => BirthBoundingTags.Contains(e.Tag))
            .Concat(person.Census)
            .Select(ToFact)
            .Concat(person.FamSpouse
                .Where(f => f.Marriage is not null)
                .Select(f => ToFact(f.Marriage!)));
        return Ordered(facts);
    }

    // Everything dated after the last sighting narrows the death: burial, will, probate, censuses, residences.
    static IReadOnlyList<BriefFact> DeathBounding(GedIndividual person)
    {
        var facts = person.Census
            .Concat(person.OtherEvents)
            .Append(person.Will)
            .Append(person.Probate)
            .OfType<GedEvent>()
            .Select(ToFact);
        return Ordered(facts);
    }

    static IReadOnlyList<BriefFact> Ordered(IEnumerable<BriefFact> facts) =>
        [.. facts
            .Where(f => f.Date != null || f.Place != null)
            .OrderBy(f => GedDate.ParseYear(f.Date))
            .ThenBy(f => f.Tag, StringComparer.Ordinal)];

    static BriefFact ToFact(GedEvent ev) => new(ev.Tag, NullIfEmpty(ev.Date), NullIfEmpty(ev.Place));

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

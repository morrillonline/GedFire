using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Matches a whole structured record — a census household, an obituary, a
// probate file — against the tree in one pass: name-tolerant recall per
// record person (the same recall as find_person), subgraph scoring against
// the record's own stated relationships, a closed same-name survey with an
// exclusion reason for every non-winner, and find_person's confidence
// floors for the single/candidates/none verdict. Pure and deterministic:
// nothing is drawn, nothing is written.
// ---------------------------------------------------------------------------

public sealed class RecordMatcher
{
    readonly PersonMatcher _matcher;
    readonly RecordCandidateScorer _scorer;

    public RecordMatcher(NicknameDirectory nicknames)
    {
        ArgumentNullException.ThrowIfNull(nicknames);
        _matcher = new PersonMatcher(nicknames);
        _scorer = new RecordCandidateScorer(nicknames);
    }

    public RecordMatchResult Match(MatchIndex index, RecordDescription record)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(record);

        IEnumerable<int> personIndexes = record.Focus is int focus
            ? [focus]
            : Enumerable.Range(0, record.Persons.Count);
        return new RecordMatchResult([.. personIndexes.Select(i => MatchPerson(index, record, i))]);
    }

    RecordPersonMatch MatchPerson(MatchIndex index, RecordDescription record, int personIndex)
    {
        var person = record.Persons[personIndex];
        var kin = StatedKin.Resolve(record, personIndex);
        // Uncapped name-only recall: the survey must be closed, so every
        // same-name person is considered and none is silently dropped.
        var outcome = _matcher.Match(index, person.Name, hints: null, maxResults: null);

        if (outcome.PersonMatchType == PersonMatchType.None)
            return NoSameNamePerson(personIndex, person, outcome);

        var excluded = new List<(GedIndividual Individual, string Reason)>();
        var scored = new List<RecordCandidateScore>();
        foreach (var match in outcome.Matches)
        {
            string? reason = RecordCandidateScorer.ExclusionReason(match.Individual, person, record.Event?.Year);
            if (reason != null) excluded.Add((match.Individual, reason));
            else scored.Add(_scorer.Score(match, person, kin, record.Event));
        }

        var ranked = scored
            .OrderByDescending(s => s.Composite)
            .ThenByDescending(s => s.NameScore)
            .ThenBy(s => s.Individual.Xref, StringComparer.Ordinal)
            .ToList();

        // find_person's confidence floors: a lone survivor, or a decisive lead.
        bool single = ranked.Count == 1 ||
            (ranked.Count > 1 &&
             ranked[0].Composite >= PersonMatchCore.SingleMatchMinScore &&
             ranked[0].Composite - ranked[1].Composite >= PersonMatchCore.SingleMatchMargin);

        return new RecordPersonMatch
        {
            Index = personIndex,
            Role = person.Role,
            Name = person.Name,
            Verdict = ranked.Count == 0 ? "none" : single ? "single" : "candidates",
            ConfidentMatchXref = ranked.Count > 0 && single ? ranked[0].Individual.Xref : null,
            ConfidentMatchScore = ranked.Count > 0 && single ? ranked[0].Composite : null,
            Candidates = [.. ranked.Select(ToCandidate)],
            SameNameSurvey = BuildSurvey(ranked, excluded, kin),
            PotentialNewPerson = ranked.Count == 0,
            PossibleDuplicates = [],
        };
    }

    static RecordPersonMatch NoSameNamePerson(int personIndex, RecordPersonDescription person, MatchOutcome outcome) => new()
    {
        Index = personIndex,
        Role = person.Role,
        Name = person.Name,
        Verdict = "none",
        Candidates = [],
        SameNameSurvey = [],
        PotentialNewPerson = true,
        PossibleDuplicates = [.. outcome.Suggestions.Select(s => new RecordPossibleDuplicate(
            s.Individual.Xref,
            PersonDisplay.FullName(s.Individual),
            s.Reason == SuggestionReason.CloseSpelling ? "close spelling" : "partial name",
            Math.Round(s.Score, 1)))],
    };

    static RecordCandidateMatch ToCandidate(RecordCandidateScore s) => new()
    {
        Xref = s.Individual.Xref,
        Name = PersonDisplay.FullName(s.Individual),
        Born = EventDisplay(s.Individual.Birth),
        Died = EventDisplay(s.Individual.DeathOrBurial),
        Score = s.Composite,
        Subscores = new RecordSubscores
        {
            Name = s.NameScore,
            Lifespan = s.LifespanScore,
            KinOverlap = s.KinOverlapScore,
            Place = s.PlaceScore,
        },
        MatchedKin = s.MatchedKin,
    };

    static IReadOnlyList<RecordSurveyEntry> BuildSurvey(
        List<RecordCandidateScore> ranked, List<(GedIndividual Individual, string Reason)> excluded, StatedKin kin)
    {
        var entries = new List<RecordSurveyEntry>(ranked.Count + excluded.Count);
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            var s = ranked[rank];
            entries.Add(new RecordSurveyEntry
            {
                Xref = s.Individual.Xref,
                Name = PersonDisplay.FullName(s.Individual),
                Born = EventDisplay(s.Individual.Birth),
                Died = EventDisplay(s.Individual.DeathOrBurial),
                Outcome = rank == 0 ? "leading" : "candidate",
                Reason = rank == 0 ? null : NonLeaderReason(s, ranked[0], kin),
            });
        }
        foreach (var (individual, reason) in excluded)
        {
            entries.Add(new RecordSurveyEntry
            {
                Xref = individual.Xref,
                Name = PersonDisplay.FullName(individual),
                Born = EventDisplay(individual.Birth),
                Died = EventDisplay(individual.DeathOrBurial),
                Outcome = "excluded",
                Reason = reason,
            });
        }
        return entries;
    }

    // The most concrete disagreement available; failing one, the honest comparative score.
    static string NonLeaderReason(RecordCandidateScore s, RecordCandidateScore top, StatedKin kin)
    {
        if (kin.Spouses.Count > 0 && HasRecordedSpouse(s.Individual) &&
            !s.MatchedKin.Any(k => k.Relation == "spouse"))
            return $"no recorded spouse matches {Names(kin.Spouses)}";
        if (kin.Parents.Count > 0 && HasRecordedParent(s.Individual) &&
            !s.MatchedKin.Any(k => k.Relation == "parent"))
            return $"recorded parents do not match {Names(kin.Parents)}";
        return $"scored {s.Composite} against the leading candidate's {top.Composite}";
    }

    static bool HasRecordedSpouse(GedIndividual indi) =>
        indi.FamSpouse.Any(f => f.SpouseOf(indi) != null);

    static bool HasRecordedParent(GedIndividual indi) =>
        indi.FamChild is { } fam && (fam.Husband != null || fam.Wife != null);

    static string Names(IReadOnlyList<RecordPersonDescription> persons) =>
        string.Join(", ", persons.Select(p => p.Name));

    static string? EventDisplay(GedEvent? ev)
    {
        if (ev is null) return null;
        string date = ev.Date;
        string place = ev.Place;
        if (date.Length == 0 && place.Length == 0) return null;
        if (date.Length == 0) return place;
        if (place.Length == 0) return date;
        return $"{date}, {place}";
    }
}

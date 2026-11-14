using GedCore;
using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Matches a whole structured record — a census household, an obituary, a
// probate file — against the tree in one pass: name-tolerant recall per
// record person (the same recall as find_person), subgraph scoring against
// the record's own stated relationships, a closed same-name survey with an
// exclusion reason for every non-winner, and find_person's confidence
// floors for the single/weak/candidates/none verdict, where single also needs
// evidence beyond the name. Pure and deterministic: nothing is drawn, nothing
// is written.
// ---------------------------------------------------------------------------

public sealed class RecordMatcher
{
    readonly PersonMatcher _matcher;
    readonly RecordCandidateScorer _scorer;
    readonly KnownRelatives _known;

    public RecordMatcher(NicknameDirectory nicknames)
    {
        ArgumentNullException.ThrowIfNull(nicknames);
        _matcher = new PersonMatcher(nicknames);
        _scorer = new RecordCandidateScorer(nicknames);
        _known = new KnownRelatives(_scorer);
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

    // The candidates recalled for one record person: those the record rules out, with why, and the rest scored and ranked.
    sealed record Considered(List<RecordCandidateScore> Ranked, List<(GedIndividual Individual, string Reason)> Excluded);

    RecordPersonMatch MatchPerson(MatchIndex index, RecordDescription record, int personIndex)
    {
        var person = record.Persons[personIndex];
        if (person.Xref is { } xref)
        {
            var bound = KnownRelatives.Find(index, xref)
                ?? throw new ArgumentException($"persons[{personIndex}].xref {xref} is not a person in the document.", nameof(record));
            return MatchKnown(index, record, personIndex, bound, $"the record binds {person.Name} to {bound.Xref}") with { Bound = true };
        }
        if (_known.Anchored(index, record, personIndex) is { } anchored)
            return MatchKnown(index, record, personIndex, anchored.Person,
                $"the record binds {anchored.AnchorName} to {anchored.AnchorXref}, whose only matching tree {anchored.Relation} is {anchored.Person.Xref}")
                with { AnchoredBy = anchored.AnchorXref };

        // Uncapped name-only recall: the survey must be closed, so every
        // same-name person is considered and none is silently dropped.
        var outcome = _matcher.Match(index, person.Name, hints: null, maxResults: null);
        if (outcome.PersonMatchType == PersonMatchType.None)
            return NoSameNamePerson(personIndex, person, outcome);

        var kin = StatedKin.Resolve(record, personIndex);
        var considered = Consider(outcome.Matches, record, personIndex, kin);
        var ranked = considered.Ranked;
        var notRelated = _known.NotRelated(index, record, personIndex, ranked.Select(s => s.Individual));
        string verdict = RecordVerdict.Decide(ranked, notRelated);
        bool single = verdict == RecordVerdict.Single;

        return Result(personIndex, person, ranked) with
        {
            Verdict = verdict,
            ConfidentMatchXref = single ? ranked[0].Individual.Xref : null,
            ConfidentMatchScore = single ? ranked[0].Composite : null,
            SameNameSurvey = RecordMatchRows.Survey(ranked, considered.Excluded, kin, notRelated),
            PotentialNewPerson = verdict == RecordVerdict.None,
        };
    }

    // The record says who this person is, by its own binding or through a bound relative: that person is the match, and the
    // same-name survey still shows who else was considered.
    RecordPersonMatch MatchKnown(MatchIndex index, RecordDescription record, int personIndex, GedIndividual known, string reason)
    {
        var person = record.Persons[personIndex];
        var kin = StatedKin.Resolve(record, personIndex);
        var outcome = _matcher.Match(index, person.Name, hints: null, maxResults: null);
        var knownMatch = outcome.Matches.FirstOrDefault(m => ReferenceEquals(m.Individual, known)) ?? _scorer.Unrecalled(known, person.Name);
        var knownScore = _scorer.Score(knownMatch, person, kin, record.Event);

        var considered = Consider(outcome.Matches.Where(m => !ReferenceEquals(m.Individual, known)), record, personIndex, kin);
        var ranked = considered.Ranked.Prepend(knownScore).ToList();
        var others = ranked.Skip(1).ToDictionary(s => s.Individual.Xref, _ => reason, StringComparer.Ordinal);

        return Result(personIndex, person, ranked) with
        {
            Verdict = RecordVerdict.Single,
            ConfidentMatchXref = known.Xref,
            ConfidentMatchScore = knownScore.Composite,
            SameNameSurvey = RecordMatchRows.Survey(ranked, considered.Excluded, kin, others),
            PotentialNewPerson = false,
        };
    }

    Considered Consider(IEnumerable<ScoredMatch> matches, RecordDescription record, int personIndex, StatedKin kin)
    {
        var person = record.Persons[personIndex];
        var excluded = new List<(GedIndividual Individual, string Reason)>();
        var scored = new List<RecordCandidateScore>();
        foreach (var match in matches)
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
        return new Considered(ranked, excluded);
    }

    // What every result shares: the person, the ranked candidates and the runner-up.
    static RecordPersonMatch Result(int personIndex, RecordPersonDescription person, List<RecordCandidateScore> ranked) => new()
    {
        Index = personIndex,
        Role = person.Role,
        Name = person.Name,
        Verdict = RecordVerdict.None,
        RunnerUpXref = ranked.Count > 1 ? ranked[1].Individual.Xref : null,
        RunnerUpScore = ranked.Count > 1 ? ranked[1].Composite : null,
        Candidates = [.. ranked.Select(RecordMatchRows.Candidate)],
        SameNameSurvey = [],
        PotentialNewPerson = false,
        PossibleDuplicates = [],
    };

    static RecordPersonMatch NoSameNamePerson(int personIndex, RecordPersonDescription person, MatchOutcome outcome) =>
        Result(personIndex, person, []) with
        {
            PotentialNewPerson = true,
            PossibleDuplicates = [.. outcome.Suggestions.Select(s => new RecordPossibleDuplicate(
                s.Individual.Xref,
                PersonDisplay.FullName(s.Individual),
                s.Reason == SuggestionReason.CloseSpelling ? "close spelling" : "partial name",
                Math.Round(s.Score, 1)))],
        };
}
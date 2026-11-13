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
        if (person.Xref is { } xref)
        {
            var bound = Find(index, xref)
                ?? throw new ArgumentException($"persons[{personIndex}].xref {xref} is not a person in the document.", nameof(record));
            return MatchKnown(index, record, personIndex, bound, $"the record binds {person.Name} to {bound.Xref}") with { Bound = true };
        }
        if (AnchoredCandidate(index, record, personIndex) is var (anchored, anchor))
            return MatchKnown(index, record, personIndex, anchored,
                $"the record binds {anchor.Name} to {anchor.Xref}, whose only matching tree {anchor.Relation} is {anchored.Xref}") with { AnchoredBy = anchor.Xref };
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
            RunnerUpXref = ranked.Count > 1 ? ranked[1].Individual.Xref : null,
            RunnerUpScore = ranked.Count > 1 ? ranked[1].Composite : null,
            Candidates = [.. ranked.Select(ToCandidate)],
            SameNameSurvey = BuildSurvey(ranked, excluded, kin),
            PotentialNewPerson = ranked.Count == 0,
            PossibleDuplicates = [],
        };
    }

    static GedIndividual? Find(MatchIndex index, string xref) =>
        index.Entries.FirstOrDefault(e => e.Individual.Xref == xref)?.Individual;

    sealed record Anchor(string Xref, string Name, string Relation);

    // A person the record relates to a bound person is that person's tree relative of the same kind, when the tree holds
    // exactly one whose given name agrees and who could be the record's person. The surname is not compared: a wife is
    // often recorded under her married name and held under her maiden one.
    (GedIndividual Person, Anchor Anchor)? AnchoredCandidate(MatchIndex index, RecordDescription record, int personIndex)
    {
        var person = record.Persons[personIndex];
        var found = new List<(GedIndividual Person, Anchor Anchor)>();
        foreach (var rel in record.Relationships.Where(r => r.From == personIndex || r.To == personIndex))
        {
            int other = rel.From == personIndex ? rel.To : rel.From;
            if (record.Persons[other].Xref is not { } xref || Find(index, xref) is not { } bound) continue;
            var (relation, relatives) = RelativesOf(bound, rel, personIndex);
            found.AddRange(relatives
                .Where(r => RecordCandidateScorer.ExclusionReason(r, person, record.Event?.Year) is null && _scorer.GivenNameAgrees(person.Name, r))
                .Select(r => (r, new Anchor(xref, record.Persons[other].Name, relation))));
        }
        var distinct = found.DistinctBy(f => f.Person.Xref).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    // The tree relatives of the bound person in the place the record gives this person.
    static (string Relation, List<GedIndividual> Relatives) RelativesOf(GedIndividual bound, RecordRelationship rel, int personIndex)
    {
        bool personIsFrom = rel.From == personIndex;
        return rel.Kind switch
        {
            RecordRelationshipKind.SpouseOf => ("spouse", TreeKin.Spouses(bound)),
            RecordRelationshipKind.SiblingOf => ("sibling", TreeKin.Siblings(bound)),
            RecordRelationshipKind.ChildOf => personIsFrom ? ("child", TreeKin.Children(bound)) : ("parent", TreeKin.Parents(bound)),
            _ => personIsFrom ? ("parent", TreeKin.Parents(bound)) : ("child", TreeKin.Children(bound)),
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

        var excluded = new List<(GedIndividual Individual, string Reason)>();
        var others = new List<RecordCandidateScore>();
        foreach (var match in outcome.Matches.Where(m => !ReferenceEquals(m.Individual, known)))
        {
            string? exclusion = RecordCandidateScorer.ExclusionReason(match.Individual, person, record.Event?.Year);
            if (exclusion != null) excluded.Add((match.Individual, exclusion));
            else others.Add(_scorer.Score(match, person, kin, record.Event));
        }
        var ranked = others
            .OrderByDescending(s => s.Composite)
            .ThenByDescending(s => s.NameScore)
            .ThenBy(s => s.Individual.Xref, StringComparer.Ordinal)
            .Prepend(knownScore)
            .ToList();

        var survey = BuildSurvey(ranked, excluded, kin)
            .Select((entry, rank) => rank is > 0 && rank < ranked.Count ? entry with { Reason = reason } : entry)
            .ToList();
        return new RecordPersonMatch
        {
            Index = personIndex,
            Role = person.Role,
            Name = person.Name,
            Verdict = "single",
            ConfidentMatchXref = known.Xref,
            ConfidentMatchScore = knownScore.Composite,
            RunnerUpXref = ranked.Count > 1 ? ranked[1].Individual.Xref : null,
            RunnerUpScore = ranked.Count > 1 ? ranked[1].Composite : null,
            Candidates = [.. ranked.Select(ToCandidate)],
            SameNameSurvey = survey,
            PotentialNewPerson = false,
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
        Lifespan = PersonDisplay.Lifespan(s.Individual),
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

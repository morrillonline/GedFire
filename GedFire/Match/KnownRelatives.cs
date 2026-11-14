using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// What the tree already says about a record person through the people the
// record binds: the one tree relative of the stated kind a bound person has
// (an anchor), and the candidates who are not that relative.
// ---------------------------------------------------------------------------

/// <summary>A record person resolved through a bound relative: <see cref="Person"/> is that relative's only matching tree relative.</summary>
public sealed record AnchoredPerson(GedIndividual Person, string AnchorXref, string AnchorName, string Relation);

public sealed class KnownRelatives(RecordCandidateScorer scorer)
{
    public static GedIndividual? Find(MatchIndex index, string xref) =>
        index.Entries.FirstOrDefault(e => e.Individual.Xref == xref)?.Individual;

    /// <summary>
    /// The bound person's tree relative of the kind the record states, when the tree holds exactly one whose given name
    /// agrees and who could be the record's person. The surname is not compared: a wife is often recorded under her
    /// married name and held under her maiden one.
    /// </summary>
    public AnchoredPerson? Anchored(MatchIndex index, RecordDescription record, int personIndex)
    {
        var person = record.Persons[personIndex];
        var found = new List<AnchoredPerson>();
        foreach (var (rel, other) in RelationshipsOf(record, personIndex))
        {
            if (record.Persons[other].Xref is not { } xref || Find(index, xref) is not { } bound) continue;
            var (relation, relatives) = RelativesOf(bound, rel, personIndex);
            found.AddRange(relatives
                .Where(r => RecordCandidateScorer.ExclusionReason(r, person, record.Event?.Year) is null && scorer.GivenNameAgrees(person.Name, r))
                .Select(r => new AnchoredPerson(r, xref, record.Persons[other].Name, relation)));
        }
        var distinct = found.DistinctBy(f => f.Person.Xref).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>
    /// Each candidate the tree does not hold as the stated relative of a person the record binds, or resolves through an
    /// anchor, with the reason. Such a candidate can only be someone else.
    /// </summary>
    public IReadOnlyDictionary<string, string> NotRelated(
        MatchIndex index, RecordDescription record, int personIndex, IEnumerable<GedIndividual> candidates)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = candidates.ToList();
        foreach (var (rel, other) in RelationshipsOf(record, personIndex))
        {
            var known = record.Persons[other].Xref is { } xref ? Find(index, xref) : Anchored(index, record, other)?.Person;
            if (known is null) continue;
            var (relation, relatives) = RelativesOf(known, rel, personIndex);
            foreach (var candidate in list.Where(c => !relatives.Contains(c)))
                reasons.TryAdd(candidate.Xref, $"not the {KinWord(relation, candidate)} of {known.Xref} ({PersonDisplay.FullName(known)}) in the tree");
        }
        return reasons;
    }

    static IEnumerable<(RecordRelationship Relationship, int Other)> RelationshipsOf(RecordDescription record, int personIndex) =>
        record.Relationships.Where(r => r.From == personIndex || r.To == personIndex)
            .Select(r => (r, r.From == personIndex ? r.To : r.From));

    // The tree relatives of the known person in the place the record gives this person.
    static (string Relation, List<GedIndividual> Relatives) RelativesOf(GedIndividual known, RecordRelationship rel, int personIndex)
    {
        bool personIsFrom = rel.From == personIndex;
        return rel.Kind switch
        {
            RecordRelationshipKind.SpouseOf => ("spouse", TreeKin.Spouses(known)),
            RecordRelationshipKind.SiblingOf => ("sibling", TreeKin.Siblings(known)),
            RecordRelationshipKind.ChildOf => personIsFrom ? ("child", TreeKin.Children(known)) : ("parent", TreeKin.Parents(known)),
            _ => personIsFrom ? ("parent", TreeKin.Parents(known)) : ("child", TreeKin.Children(known)),
        };
    }

    static string KinWord(string relation, GedIndividual candidate) => (relation, candidate.SexRecorded ? candidate.IsMale : (bool?)null) switch
    {
        ("parent", true) => "father", ("parent", false) => "mother",
        ("child", true) => "son", ("child", false) => "daughter",
        ("spouse", true) => "husband", ("spouse", false) => "wife",
        ("sibling", true) => "brother", ("sibling", false) => "sister",
        _ => relation,
    };
}

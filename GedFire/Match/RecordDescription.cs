namespace GedFire.Match;

// ---------------------------------------------------------------------------
// The structured description of one historical record — a census household,
// an obituary's survivors, a probate file's heirs — that match_record scores
// against the tree. Pure input data; RecordMatcher does the matching.
// ---------------------------------------------------------------------------

public sealed record RecordDescription
{
    public required IReadOnlyList<RecordPersonDescription> Persons { get; init; }
    public IReadOnlyList<RecordRelationship> Relationships { get; init; } = [];
    public RecordEventDescription? Event { get; init; }

    /// <summary>Index of the one person to identify; null identifies them all.</summary>
    public int? Focus { get; init; }
}

public sealed record RecordPersonDescription
{
    public required string Name { get; init; }

    /// <summary>The record's own label — "head", "wife", "survivor", "deceased". Free text, echoed back.</summary>
    public string? Role { get; init; }

    public bool? IsMale { get; init; }
    public int? BirthYear { get; init; }
    public string? BirthPlace { get; init; }
}

public enum RecordRelationshipKind { SpouseOf, ChildOf, ParentOf, SiblingOf }

/// <summary>
/// A relationship the record itself states, by index into
/// <see cref="RecordDescription.Persons"/>: for ChildOf, From is the child of
/// To; for ParentOf, From is a parent of To; SpouseOf and SiblingOf are
/// symmetric.
/// </summary>
public sealed record RecordRelationship(RecordRelationshipKind Kind, int From, int To);

public sealed record RecordEventDescription
{
    public int? Year { get; init; }
    public string? Place { get; init; }

    /// <summary>Free text ("census", "obituary"). Context only; never scored.</summary>
    public string? Type { get; init; }
}

/// <summary>The kin the record states for one person, resolved from relationship indexes to the persons themselves.</summary>
public sealed record StatedKin(
    IReadOnlyList<RecordPersonDescription> Spouses,
    IReadOnlyList<RecordPersonDescription> Parents,
    IReadOnlyList<RecordPersonDescription> Children,
    IReadOnlyList<RecordPersonDescription> Siblings)
{
    public bool Any => Spouses.Count + Parents.Count + Children.Count + Siblings.Count > 0;

    public static StatedKin Resolve(RecordDescription record, int index)
    {
        List<RecordPersonDescription> spouses = [], parents = [], children = [], siblings = [];
        foreach (var rel in record.Relationships)
        {
            if (rel.From != index && rel.To != index) continue;
            var other = record.Persons[rel.From == index ? rel.To : rel.From];
            switch (rel.Kind)
            {
                case RecordRelationshipKind.SpouseOf: spouses.Add(other); break;
                case RecordRelationshipKind.SiblingOf: siblings.Add(other); break;
                case RecordRelationshipKind.ChildOf: (rel.From == index ? parents : children).Add(other); break;
                case RecordRelationshipKind.ParentOf: (rel.From == index ? children : parents).Add(other); break;
            }
        }
        return new StatedKin(spouses, parents, children, siblings);
    }
}

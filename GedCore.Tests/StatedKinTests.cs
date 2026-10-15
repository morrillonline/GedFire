using GedFire.Match;

namespace GedCore.Tests;

public class StatedKinTests
{
    static RecordPersonDescription Person(string name) => new() { Name = name };

    static readonly RecordDescription Household = new()
    {
        Persons = [Person("John"), Person("Mary"), Person("Thomas"), Person("Anna")],
        Relationships =
        [
            new RecordRelationship(RecordRelationshipKind.SpouseOf, 1, 0),
            new RecordRelationship(RecordRelationshipKind.ChildOf, 2, 0),
            new RecordRelationship(RecordRelationshipKind.ParentOf, 0, 3),
            new RecordRelationship(RecordRelationshipKind.SiblingOf, 2, 3),
        ],
    };

    [Fact]
    public void Resolve_BucketsEveryRelationshipTouchingThePerson()
    {
        var kin = StatedKin.Resolve(Household, 0);

        Assert.True(kin.Any);
        Assert.Equal(["Mary"], kin.Spouses.Select(p => p.Name));
        Assert.Equal(["Thomas", "Anna"], kin.Children.Select(p => p.Name));
        Assert.Empty(kin.Parents);
        Assert.Empty(kin.Siblings);
    }

    [Fact]
    public void Resolve_SeesChildOfAndParentOfFromTheOtherEnd()
    {
        var thomas = StatedKin.Resolve(Household, 2);
        Assert.Equal(["John"], thomas.Parents.Select(p => p.Name));
        Assert.Equal(["Anna"], thomas.Siblings.Select(p => p.Name));

        var anna = StatedKin.Resolve(Household, 3);
        Assert.Equal(["John"], anna.Parents.Select(p => p.Name));
        Assert.Equal(["Thomas"], anna.Siblings.Select(p => p.Name));
    }

    [Fact]
    public void Resolve_SpouseIsSymmetric()
    {
        var mary = StatedKin.Resolve(Household, 1);
        Assert.Equal(["John"], mary.Spouses.Select(p => p.Name));
    }

    [Fact]
    public void Resolve_WithNoRelationshipsTouchingThePerson_HasNone()
    {
        var record = new RecordDescription { Persons = [Person("Alone")] };
        var kin = StatedKin.Resolve(record, 0);
        Assert.False(kin.Any);
    }
}

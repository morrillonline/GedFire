using GedCore.Drafting;

namespace GedCore.Tests;

public class DraftReferencesTests
{
    static readonly DraftSource Source = new("Fictional County Register", null, null, "p. 1", null, "original", "primary");

    static DraftFinding Finding(params DraftRelation[] relations) =>
        new("F-1", Source, "x", new DraftTarget("@I1@", null, null),
            [new DraftFact("@NewF1@", "MARR", "1850", null, 3, null, null, null)], [new DraftNote("@I1@", "A note.")])
        {
            People = [new DraftPerson("@NewI1@", "Hester /Ambrose/", "F")],
            ExistingPeople = ["@I2@"],
            Relations = relations,
        };

    [Fact]
    public void EveryNamedXref_IsListedWithItsPathInOrder()
    {
        var spouse = new DraftRelation(DraftRelation.Spouse, "@I1@", "@NewI1@", "@NewF1@", null, null, null, 3) { NotDuplicateOf = ["@I3@"] };

        var references = DraftReferences.Of(Finding(spouse));

        Assert.Equal(
            ["target.xref", "existingPeople[0]", "facts[0].record", "relations[0].person", "relations[0].spouse",
             "relations[0].family", "relations[0].notDuplicateOf[0]", "notes[0].record"],
            references.Select(r => r.Path));
    }

    [Fact]
    public void APlaceholder_IsDeclaredByPeopleOrAsARelationsFamily()
    {
        var child = new DraftRelation(DraftRelation.Child, "", "@NewI2@", "@NewF1@", null, "@I1@", null, 3);

        var references = DraftReferences.Of(Finding(child));

        Assert.Contains(new DraftReference("@NewF1@", "facts[0].record", true, true), references);
        Assert.Contains(new DraftReference("@NewI2@", "relations[0].child", true, false), references);
        Assert.Contains(new DraftReference("@I1@", "relations[0].husb", false, false), references);
    }

    [Fact]
    public void Void_AndAnUnlinkParentsEmptyOther_AreNotReferences()
    {
        var unlink = new DraftRelation(DraftRelation.UnlinkParent, "@I1@", "", null, "father", null, null, 0);
        var child = new DraftRelation(DraftRelation.Child, "", "@I1@", "@F1@", null, DraftReferences.Void, null, 3);

        var references = DraftReferences.Of(Finding(unlink, child));

        Assert.DoesNotContain(references, r => r.Xref is "" or DraftReferences.Void);
        Assert.Contains(references, r => r.Path == "relations[0].person");
    }
}

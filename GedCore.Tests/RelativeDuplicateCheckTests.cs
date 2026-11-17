using System.Text;
using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Tests;

public class RelativeDuplicateCheckTests
{
    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Abel /Morrill/
        1 SEX M
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME Nathaniel /Morrill/
        1 SEX M
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Eula Marie /Hicks/
        1 SEX F
        1 FAMS @F2@
        0 @I4@ INDI
        1 NAME Donald Darrell /Morrell/
        1 FAMC @F2@
        0 @F1@ FAM
        1 HUSB @I2@
        1 CHIL @I1@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I3@
        1 CHIL @I4@
        0 TRLR
        """;

    static readonly DocumentRelatives Tree = new(Ged70Parser.Read(new MemoryStream(Encoding.UTF8.GetBytes(Document))));

    static IReadOnlyList<string> Check(string name, string op)
    {
        var changeset = Changeset.Parse($$"""
            { "items": [ { "item": 1,
              "people": [ { "xref": "@NewI1@", "name": "{{name}}" } ],
              "ops": [ {{op}} ] } ] }
            """);
        return RelativeDuplicateCheck.Check(changeset.Items[0].Ops, Tree);
    }

    const string Father = """{ "op": "createOrUpdateParent", "person": "@I1@", "role": "father", "parent": "@NewI1@" }""";
    const string Mother = """{ "op": "createOrUpdateParent", "person": "@I1@", "role": "mother", "parent": "@NewI1@" }""";
    const string Spouse = """{ "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@" }""";
    const string ChildOfFamily = """{ "op": "createOrUpdateChild", "family": "@F2@", "child": "@NewI1@" }""";
    const string ChildOfNewFamily = """{ "op": "createOrUpdateChild", "family": "@NewF1@", "child": "@NewI1@", "husb": "@I1@" }""";

    [Theory]
    [InlineData("Nathaniel /Morrill/", Father, "Nathaniel Morrill (@NewI1@): Abel Morrill (@I1@) already has the father Nathaniel Morrill (@I2@), the same name")]
    [InlineData("Seth /Morrill/", Father, "Seth Morrill (@NewI1@): Abel Morrill (@I1@) already has the father Nathaniel Morrill (@I2@), so a new father would be a second, conflicting one")]
    [InlineData("Eula M /Hicks/", Spouse, "Eula M Hicks (@NewI1@): Abel Morrill (@I1@) already has the spouse Eula Marie Hicks (@I3@), the same name")]
    [InlineData("Donald D /Morrell/", ChildOfFamily, "Donald D Morrell (@NewI1@): family @F2@ already has the child Donald Darrell Morrell (@I4@), the same name")]
    [InlineData("Donald D /Morrell/", ChildOfNewFamily, "Donald D Morrell (@NewI1@): Abel Morrill (@I1@), a partner in the new family @NewF1@, already has the child Donald Darrell Morrell (@I4@)")]
    public void ARelativeThePersonAlreadyHas_IsRefusedNamingItAndTheCure(string name, string op, string expected)
    {
        string error = Assert.Single(Check(name, op));

        Assert.StartsWith(expected, error);
        Assert.Contains("in place of @NewI1@", error);
        Assert.Contains("in the relation's notDuplicateOf", error);
    }

    [Theory]
    [InlineData("Nathaniel /Morrill/", Father, "@I2@")]
    [InlineData("Seth /Morrill/", Father, "@I2@")]
    [InlineData("Eula M /Hicks/", Spouse, "@I3@")]
    [InlineData("Donald D /Morrell/", ChildOfFamily, "@I4@")]
    [InlineData("Donald D /Morrell/", ChildOfNewFamily, "@I4@")]
    public void TheRelativeListedInNotDuplicateOf_Passes(string name, string op, string relative) =>
        Assert.Empty(Check(name, op.Replace(" }", $", \"notDuplicateOf\": [\"{relative}\"] }}")));

    [Theory]
    [InlineData("Ann /Poole/", Spouse)]
    [InlineData("Mary /Morrill/", Mother)]
    [InlineData("Ruth /Morrell/", ChildOfFamily)]
    public void ARelativeThePersonDoesNotHave_Passes(string name, string op) => Assert.Empty(Check(name, op));

    [Fact]
    public void AnExistingPersonLinkedAsARelative_IsNotChecked() =>
        Assert.Empty(RelativeDuplicateCheck.Check(
            Changeset.Parse("""{ "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateChild", "family": "@F2@", "child": "@I4@" } ] } ] }""")
                .Items[0].Ops,
            Tree));
}

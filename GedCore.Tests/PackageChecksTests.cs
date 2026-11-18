using GedCore.Packages;

namespace GedCore.Tests;

public class PackageChecksTests
{
    sealed class FakeSources(Dictionary<string, SourceDescription>? held = null) : ISourceRecords
    {
        public SourceDescription? Source(string xref) => held?.GetValueOrDefault(xref);
    }

    static IReadOnlyList<string> Check(string[] changesets, ISourceRecords? sources = null, params SameNamePair[] sameNamePeople) =>
        PackageChecks.Check(
            [.. changesets.Select((json, i) => new PackageChangeset(i + 1, json))],
            sources ?? new FakeSources(),
            new PackageOptions(sameNamePeople));

    static string Changeset(string ops, string people = "[]", string newSources = "[]", int item = 1) => $$"""
        { "newSources": {{newSources}},
          "items": [ { "item": {{item}}, "people": {{people}}, "ops": [ {{ops}} ] } ] }
        """;

    static string NewSource(string xref, string title, string? url = null) =>
        $$"""[ { "xref": "{{xref}}", "ops": [ { "op": "createOrUpdateSource", "xref": "{{xref}}", "title": "{{title}}"{{(url is null ? "" : $", \"url\": \"{url}\"")}} } ] } ]""";

    static string Birth(string record, string source, string page, int quay, string extract = "born 1 Jan 1850") =>
        $$"""{ "op": "createOrUpdateVital", "record": "{{record}}", "fact": "BIRT", "date": "1 JAN 1850", "citations": [ { "source": "{{source}}", "page": "{{page}}", "dataText": "{{extract}}", "quay": {{quay}} } ] }""";

    static string People(string xref, string name) => $$"""[ { "xref": "{{xref}}", "name": "{{name}}" } ]""";

    // ---- one grade per extract ----

    [Fact]
    public void OneExtractGradedDifferentlyInTwoItems_IsRefusedNamingBoth()
    {
        string error = Assert.Single(Check([Changeset(Birth("@I1@", "@S1@", "p. 4", 1)), Changeset(Birth("@I2@", "@S1@", "p. 4", 2))]));

        Assert.StartsWith("changeset 1 item 1 and changeset 2 item 1 cite @S1@, page \"p. 4\", with the same extract at QUAY 1 and QUAY 2", error);
    }

    [Fact]
    public void OneExtractGradedAlikeInTwoItems_Passes() =>
        Assert.Empty(Check([Changeset(Birth("@I1@", "@S1@", "p. 4", 2)), Changeset(Birth("@I2@", "@S1@", "p. 4", 2))]));

    [Fact]
    public void TwoPlaceholdersWithOneUrl_AreOneSource()
    {
        string first = Changeset(Birth("@I1@", "@NewSource1@", "p. 4", 1),
            newSources: NewSource("@NewSource1@", "Parish Register", "https://records.example.org/register/?page=1"));
        string second = Changeset(Birth("@I2@", "@NewSource1@", "p. 4", 2),
            newSources: NewSource("@NewSource1@", "Register of the Parish", "https://records.example.org/register"));

        Assert.Contains("cite Parish Register, page \"p. 4\"", Assert.Single(Check([first, second])));
    }

    [Fact]
    public void TwoPlaceholdersWithDifferentUrls_AreTwoSources()
    {
        string first = Changeset(Birth("@I1@", "@NewSource1@", "p. 4", 1),
            newSources: NewSource("@NewSource1@", "Parish Register", "https://records.example.org/a"));
        string second = Changeset(Birth("@I2@", "@NewSource1@", "p. 4", 2),
            newSources: NewSource("@NewSource1@", "Parish Register", "https://records.example.org/b"));

        Assert.Empty(Check([first, second]));
    }

    [Fact]
    public void APlaceholderAndAnExistingSourceWithOneTitle_AreOneSource()
    {
        var sources = new FakeSources(new() { ["@S7@"] = new SourceDescription("Parish Register", null) });
        string first = Changeset(Birth("@I1@", "@NewSource1@", "p. 4", 1), newSources: NewSource("@NewSource1@", "parish register."));

        Assert.Single(Check([first, Changeset(Birth("@I2@", "@S7@", "p. 4", 2))], sources));
    }

    // ---- one name per new person ----

    static string NewChild(string xref, string name) =>
        Changeset($$"""{ "op": "createOrUpdateChild", "family": "@F1@", "child": "{{xref}}" }""", People(xref, name));

    [Fact]
    public void TwoNewPeopleWithOneFirstGivenNameAndSurname_AreRefusedNamingBoth()
    {
        string error = Assert.Single(Check([NewChild("@NewI_a@", "Mason /Vale/"), NewChild("@NewI_b@", "Mason Sherlock /Vale/")]));

        Assert.StartsWith("changeset 1 item 1 creates @NewI_a@ \"Mason Vale\" and changeset 2 item 1 creates @NewI_b@ \"Mason Sherlock Vale\"", error);
        Assert.Contains("sameNamePeople: [[\"@NewI_a@\", \"@NewI_b@\"]]", error);
    }

    [Fact]
    public void ADeclaredSameNamePair_Passes() =>
        Assert.Empty(Check([NewChild("@NewI_a@", "Mason /Vale/"), NewChild("@NewI_b@", "Mason Sherlock /Vale/")],
            null, new SameNamePair("@NewI_b@", "@NewI_a@")));

    [Fact]
    public void TwoNewPeopleWithDifferentFirstNames_Pass() =>
        Assert.Empty(Check([NewChild("@NewI_a@", "Mason /Vale/"), NewChild("@NewI_b@", "John /Vale/")]));

    [Fact]
    public void ADeclaredPairNoChangesetCreates_IsRefused() =>
        Assert.Contains("sameNamePeople names @NewI_z@, which no changeset in the package creates",
            Assert.Single(Check([NewChild("@NewI_a@", "Mason /Vale/")], null, new SameNamePair("@NewI_a@", "@NewI_z@"))));
}

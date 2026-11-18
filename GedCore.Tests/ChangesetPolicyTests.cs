using System.Text;
using GedCore.Policy;

namespace GedCore.Tests;

public class ChangesetPolicyTests
{
    static ChangesetPolicy Load(string json) => ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    const string Phrases = """{ "bannedPhrases": ["Both readings are kept", "not added"], "bannedPatterns": ["@[ISFMN][A-Za-z]*\\d+@"] }""";

    const string DeathEvidence = """{ "deathPlaceEvidence": "\\b(died|death)\\b" }""";

    static string Changeset(string opsJson) => $$"""{ "items": [ { "item": 1, "ops": [ {{opsJson}} ] } ] }""";

    // ---- loading ----

    [Fact]
    public void Load_RejectsUnknownKey_NamingIt()
    {
        var ex = Assert.Throws<FormatException>(() => Load("""{ "bannedPhrase": [] }"""));

        Assert.Contains("unknown key \"bannedPhrase\"", ex.Message);
    }

    [Fact]
    public void Load_RejectsInvalidJson() =>
        Assert.Throws<FormatException>(() => Load("{ not json"));

    [Fact]
    public void Load_RejectsAnInvalidRegularExpression()
    {
        var ex = Assert.Throws<FormatException>(() => Load("""{ "bannedPatterns": ["("] }"""));

        Assert.Contains("bannedPatterns", ex.Message);
    }

    [Fact]
    public void Load_RefusesAVerificationNoteRule_SayingToDeleteIt()
    {
        var ex = Assert.Throws<FormatException>(() => Load("""{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }"""));

        Assert.Contains("\"verificationNote\", which is no longer supported", ex.Message);
        Assert.Contains("Delete the key", ex.Message);
    }

    [Fact]
    public void EmptyPolicy_EnforcesNothing() =>
        Assert.Empty(Load("{}").Check(Changeset("""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "anything @S1@ at all" }""")));

    // ---- banned prose ----

    [Fact]
    public void BannedPhrase_InNoteText_IsReportedWithItsItem()
    {
        var violations = Load(Phrases).Check(Changeset(
            """{ "op": "createOrUpdateNote", "record": "@I1@", "text": "Both readings are kept in the record." }"""));

        var violation = Assert.Single(violations);
        Assert.Contains("item 1", violation);
        Assert.Contains("banned phrase \"Both readings are kept\"", violation);
        Assert.StartsWith("policy: ", violation);
    }

    [Fact]
    public void BannedPhrase_IsCaseInsensitive() =>
        Assert.Single(Load(Phrases).Check(Changeset(
            """{ "op": "createOrUpdateNote", "record": "@I1@", "text": "NOT ADDED here." }""")));

    [Fact]
    public void BannedPattern_CatchesAnInternalId() =>
        Assert.Contains("matches banned pattern", Assert.Single(Load(Phrases).Check(Changeset(
            """{ "op": "createOrUpdateNote", "record": "@I1@", "text": "See @S00123@ for detail." }"""))));

    [Fact]
    public void BannedPhrase_InACitationPage_IsReported() =>
        Assert.Single(Load(Phrases).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1900",
                 "citations": [{ "source": "@S1@", "page": "p. 4, not added", "quay": 3 }] }
            """)));

    [Fact]
    public void BannedPhrase_InCitationCommentary_IsReported() =>
        Assert.Contains("citation commentary contains banned phrase \"not added\"", Assert.Single(Load(Phrases).Check(Changeset(
            """
            { "op": "createOrUpdateCitation", "record": "@I1@", "fact": "BIRT",
                 "citations": [{ "source": "@VOID@", "commentary": "The age was not added up." }] }
            """))));

    [Fact]
    public void BannedPhrase_InCommentaryBeingDeleted_IsNotProse() =>
        Assert.Empty(Load(Phrases).Check(Changeset(
            """
            { "op": "deleteCitation", "record": "@I1@", "fact": "BIRT", "source": "@VOID@", "commentary": "The age was not added up." }
            """)));

    [Fact]
    public void BannedPhrase_InAVerbatimExtract_IsNotProse() =>
        Assert.Empty(Load(Phrases).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1900",
                 "citations": [{ "source": "@S1@", "page": "p. 4", "dataText": "was not added to the roll", "quay": 3 }] }
            """)));

    [Fact]
    public void Check_OnlyReadsTheSelectedItems()
    {
        string json = """
            { "items": [
              { "item": 1, "ops": [ { "op": "createOrUpdateNote", "record": "@I1@", "text": "fine" } ] },
              { "item": 2, "ops": [ { "op": "createOrUpdateNote", "record": "@I1@", "text": "not added" } ] } ] }
            """;

        Assert.Empty(Load(Phrases).Check(json, new HashSet<int> { 1 }));
        Assert.Single(Load(Phrases).Check(json, new HashSet<int> { 2 }));
    }

    // ---- death place ----

    [Fact]
    public void DeathPlace_WithOnlyABurialCitation_IsReported()
    {
        var violations = Load(DeathEvidence).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "date": "1900", "place": "Salem",
                 "citations": [{ "source": "@S1@", "dataText": "Interred at Salem Cemetery", "quay": 2 }] }
            """));

        Assert.Contains("DEAT place \"Salem\"", Assert.Single(violations));
    }

    [Fact]
    public void DeathPlace_GivenBesideTheDate_IsReportedToo() =>
        Assert.Single(Load(DeathEvidence).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "date": "1900", "place": "Salem",
                 "citations": [ { "source": "@S1@", "dataText": "Interred at Salem Cemetery", "quay": 2 } ] }
            """)));

    [Fact]
    public void DeathPlace_WithADeathExtract_Passes() =>
        Assert.Empty(Load(DeathEvidence).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "date": "1900", "place": "Salem",
                 "citations": [{ "source": "@S1@", "dataText": "He died at Salem", "quay": 2 }] }
            """)));

    [Fact]
    public void DeathDateWithoutAPlace_NeedsNoEvidence() =>
        Assert.Empty(Load(DeathEvidence).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "date": "1900",
                 "citations": [{ "source": "@S1@", "dataText": "Interred", "quay": 2 }] }
            """)));

    [Fact]
    public void BurialPlace_IsNotSubjectToTheDeathPlaceRule() =>
        Assert.Empty(Load(DeathEvidence).Check(Changeset(
            """
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BURI", "place": "Salem",
                 "citations": [{ "source": "@S1@", "dataText": "Interred", "quay": 2 }] }
            """)));
}

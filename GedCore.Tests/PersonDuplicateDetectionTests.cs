namespace GedCore.Tests;

/// <summary>
/// Dedicated coverage for the new-person duplicate detector: the shared
/// PersonMatchCore engine wired against structured GedRecord data, the
/// strict "single" + score&gt;=90 duplicate rule, evidence gathered from
/// operations after the creating PersonRef, provisional candidates for
/// sibling placeholders, and the family/source/media exemption.
/// </summary>
public class PersonDuplicateDetectionTests : ApplyTestBase
{
    // Frederick Morrill, born 12 MAR 1841 Gorham Maine, married to Sarah
    // Blake -- a rich enough real record that a near-identical inline
    // creation clears the 90-point single-match floor.
    static readonly string[] RichFixtureLines =
    [
        "0 HEAD",
        "1 GEDC",
        "2 VERS 7.0",
        "0 @I00001@ INDI",
        "1 NAME Frederick /Morrill/",
        "1 SEX M",
        "1 BIRT",
        "2 DATE 12 MAR 1841",
        "2 PLAC Gorham, Maine",
        "1 FAMS @F00001@",
        "0 @I00002@ INDI",
        "1 NAME Sarah /Blake/",
        "1 SEX F",
        "1 FAMS @F00001@",
        "0 @F00001@ FAM",
        "1 HUSB @I00001@",
        "1 WIFE @I00002@",
        "0 @S00001@ SOUR",
        "1 TITL Existing source",
        "0 TRLR",
    ];

    byte[] WriteRichFixture()
    {
        var document = Ged70.Ged70Parser.Parse(string.Join("\r\n", RichFixtureLines) + "\r\n");
        var output = new MemoryStream();
        Ged70.Ged70Formatter.Write(document, output);
        return output.ToArray();
    }

    Apply.ApplyResult RunAgainstRichFixture(string changesetJson)
    {
        var changeset = Apply.Changeset.Parse(changesetJson);
        return Apply.ChangesetApplier.Run(WriteRichFixture(), changeset, [1], dryRun: true);
    }

    // -------------------------------------------------------------------
    // The core rule: matchType "single" at score >= 90 rejects; anything
    // weaker does not.
    // -------------------------------------------------------------------

    [Fact]
    public void ExactNameAndBirthMatch_AgainstRealPerson_IsRejectedAsADuplicate()
    {
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Frederick /Morrill/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "12 MAR 1841",
                                        "place": "Gorham, Maine" } } ] } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e =>
            e.Contains("@New1@") && e.Contains("@I00001@") && e.Contains("high-confidence match"));
    }

    [Fact]
    public void WhollyUnrelatedName_NeverClearsTheRecallGate_Succeeds()
    {
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Zzqxvw /Bbdfgh/", "sex": "M" } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void SameNameButDistinguishingBirthYear_IsNotAMatch_Succeeds()
    {
        // Same name, but a birth year 60 years off is real, distinguishing
        // evidence -- not the same Frederick Morrill.
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Frederick /Morrill/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1901" } } ] } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void UnresolvedCandidates_NeverRejectsSolelyAsADuplicate()
    {
        // Two real Fredericks in the fixture, tied, so the query resolves
        // to "candidates" rather than a decisive "single" -- apply never
        // chooses among competitors, so creation proceeds.
        var lines = RichFixtureLines.ToList();
        int insertAt = lines.FindIndex(l => l.StartsWith("0 @F00001@"));
        lines.InsertRange(insertAt, new[]
        {
            "0 @I00003@ INDI",
            "1 NAME Frederick /Morrill/",
            "1 SEX M",
        });
        var document = Ged70.Ged70Parser.Parse(string.Join("\r\n", lines) + "\r\n");
        var output = new MemoryStream();
        Ged70.Ged70Formatter.Write(document, output);

        var changeset = Apply.Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Frederick /Morrill/", "sex": "M" } } ] } ] }
            """);
        var result = Apply.ChangesetApplier.Run(output.ToArray(), changeset, [1], dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    // -------------------------------------------------------------------
    // Evidence gathered from later operations, not just the creating PersonRef
    // -------------------------------------------------------------------

    [Fact]
    public void SparseCreatingOp_ButLaterVitalOpSuppliesDistinguishingBirth_Succeeds()
    {
        // The creating op alone (bare name, no facts) would look like a
        // plausible match; a later createOrUpdateVital in the same item
        // gives a birth year 60 years off the real Frederick's -- gathered
        // before any verdict, this must not create a false duplicate.
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Frederick /Morrill/", "sex": "M" } },
              { "op": "createOrUpdateVital", "record": "@New1@", "fact": "BIRT",
                "value": { "date": "1901" } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void SparseCreatingOp_ButLaterVitalOpConfirmsTheMatch_IsRejected()
    {
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Frederick /Morrill/", "sex": "M" } },
              { "op": "createOrUpdateVital", "record": "@New1@", "fact": "BIRT",
                "value": { "date": "12 MAR 1841", "place": "Gorham, Maine" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("high-confidence match"));
    }

    // -------------------------------------------------------------------
    // Relational hints: an existing spouse/parent that resolves in the
    // pre-apply document narrows or confirms a match.
    // -------------------------------------------------------------------

    [Fact]
    public void SpouseRelationshipToAnExistingResolvedPerson_ConfirmsTheMatch_IsRejected()
    {
        // Only the name matches on its own (no birth data given here), but
        // marrying the SAME Sarah Blake the real Frederick already married
        // is exactly the kind of relational evidence that confirms identity.
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@New1@", "name": "Frederick /Morrill/", "sex": "M" },
                "family": "@New2@" } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("high-confidence match"));
    }

      [Fact]
      public void FatherEvidence_DoesNotMatchCandidatesMother_Succeeds()
      {
        var result = RunAgainstParentRoleFixture("husb");

        Assert.True(result.Success, string.Join("; ", result.Errors));
      }

      [Fact]
      public void MotherEvidence_MatchesCandidatesMother_IsRejected()
      {
        var result = RunAgainstParentRoleFixture("wife");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("high-confidence match"));
      }

      static Apply.ApplyResult RunAgainstParentRoleFixture(string assertedRole)
      {
        const string ged = """
          0 HEAD
          1 GEDC
          2 VERS 7.0
          0 @I1@ INDI
          1 NAME Frederick /Morrill/
          1 FAMC @F1@
          0 @I2@ INDI
          1 NAME Alex /Smith/
          1 SEX F
          1 FAMS @F1@
          0 @I3@ INDI
          1 NAME Alex /Smith/
          1 SEX M
          0 @I4@ INDI
          1 NAME Carl /Jones/
          1 SEX M
          1 FAMS @F1@
          0 @F1@ FAM
          1 HUSB @I4@
          1 WIFE @I2@
          1 CHIL @I1@
          0 TRLR
          """;
        var document = Ged70.Ged70Parser.Parse(ged.Replace("\n", "\r\n"));
        var output = new MemoryStream();
        Ged70.Ged70Formatter.Write(document, output);
        string roleProperties = assertedRole == "husb"
          ? "\"husb\": \"@I3@\""
          : "\"wife\": \"@I2@\"";
        var changeset = Apply.Changeset.Parse($$"""
          { "items": [ { "item": 1, "ops": [
            { "op": "createOrUpdateChild", "family": "@New2@", {{roleProperties}},
            "child": { "xref": "@New1@", "name": "Frederick /Morrill/" } } ] } ] }
          """);

        return Apply.ChangesetApplier.Run(output.ToArray(), changeset, [1], dryRun: true);
      }

    // -------------------------------------------------------------------
    // Conflicting scalar evidence across occurrences is a validation error,
    // not an arbitrary choice.
    // -------------------------------------------------------------------

    [Fact]
    public void ConflictingBirthYearAcrossOccurrences_FailsValidation()
    {
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
                "child": { "xref": "@New1@", "name": "Someone /Else/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1900" } } ] } },
              { "op": "createOrUpdateVital", "record": "@New1@", "fact": "BIRT",
                "value": { "date": "1905" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("conflicting birth year"));
    }

    // -------------------------------------------------------------------
    // Family/source/media placeholders are exempt entirely.
    // -------------------------------------------------------------------

    [Fact]
    public void FamilyPlaceholder_NeverParticipatesInPersonDuplicateDetection()
    {
        // A brand-new family sharing both real partners' names as its only
        // "identity" evidence would be nonsensical to score as a person; the
        // detector must simply never look at Family/Source/Media placeholders.
        var result = RunAgainstRichFixture("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00002@",
                "family": "@New1@",
                "marriage": { "date": "1 JAN 1900",
                              "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void SourceCreation_NeverParticipatesInPersonDuplicateDetection()
    {
        var result = RunAgainstRichFixture("""
            { "newSources": [ { "xref": "@New1@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@New1@", "title": "Frederick Morrill" } ] } ],
              "items": [ { "item": 1, "ops": [
                { "op": "createOrUpdateNote", "record": "@I00001@", "text": "unrelated" } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    // -------------------------------------------------------------------
    // Provisional candidates: two placeholders in the same changeset are
    // scored against each other, but a placeholder never matches itself.
    // -------------------------------------------------------------------

    [Fact]
    public void TwoNewPlaceholders_WithMatchingIdentityEvidence_SecondIsRejectedAsADuplicateOfTheFirst()
    {
        // Two new people in one changeset, same name, same birth -- the
        // second op's placeholder resolves to a "single" match against the
        // FIRST op's placeholder (a provisional candidate), not itself.
        const string op1 = """
            { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00001@",
              "child": {
                "xref": "@New1@", "name": "Wholly /Newperson/", "sex": "M",
                "facts": [
                  { "fact": "BIRT", "value": { "date": "1955" },
                    "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } }
                ]
              },
              "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 }
            }
            """;
        const string op2 = """
            { "op": "createOrUpdateChild", "family": "@New4@", "husb": "@I00001@",
              "child": {
                "xref": "@New3@", "name": "Wholly /Newperson/", "sex": "M",
                "facts": [
                  { "fact": "BIRT", "value": { "date": "1955" },
                    "citation": { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 } }
                ]
              },
              "citation": { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 }
            }
            """;
        var result = RunAgainstRichFixture($$"""
            { "items": [ { "item": 1, "ops": [ {{op1}}, {{op2}} ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("high-confidence match"));
    }
}

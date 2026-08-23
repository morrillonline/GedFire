using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>
/// Dedicated coverage for the @New&lt;token&gt;@ placeholder mechanism itself:
/// syntax enforcement at every creation position, the changeset-wide kind
/// registry, forward-reference rejection, cross-occurrence identity-conflict
/// detection, and the one static-unrulable collision. The existing
/// Apply*Tests/ProposalStyleChangesetTests suites already exercise the
/// mechanism end-to-end through realistic changesets (and prove xref values
/// mint deterministically); this file isolates the mechanism's own
/// validation rules.
/// </summary>
public class PlaceholderMintingTests : ApplyTestBase
{
    // -------------------------------------------------------------------
    // Real-looking xref rejected for creation (requirement 14 / "Validation")
    // -------------------------------------------------------------------

    [Fact]
    public void PersonCreation_WithRealLookingUnusedXref_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@I09999@", "name": "New /Wife/", "sex": "F" },
                "family": "@NewF1@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@I09999@") && e.Contains("not a placeholder"));
    }

    [Fact]
    public void FamilyCreation_WithRealLookingUnusedXref_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@NewI1@", "name": "New /Wife/", "sex": "F" },
                "family": "@F09999@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@F09999@") && e.Contains("not a placeholder"));
    }

    [Fact]
    public void SourceCreation_WithRealLookingUnusedXref_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "newSources": [ { "xref": "@S09999@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S09999@", "title": "New source" } ] } ],
              "items": [ { "item": 1, "ops": [
                { "op": "createOrUpdateNote", "record": "@I00001@", "text": "unrelated" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@S09999@") && e.Contains("not a placeholder"));
    }

    [Fact]
    public void MediaCreation_WithExplicitRealLookingUnusedXref_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@M09999@",
                "files": [ { "path": "media/x.jpg", "mediaType": "image/jpeg" } ] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@M09999@") && e.Contains("not a placeholder"));
    }

    // -------------------------------------------------------------------
    // Kind-consistency across occurrences of the same token
    // -------------------------------------------------------------------

    [Fact]
    public void SamePlaceholder_UsedAsPersonThenSource_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "newSources": [ { "xref": "@New1@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@New1@", "title": "Confused source" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@New1@", "name": "New /Wife/", "sex": "F" },
                "family": "@New2@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") &&
            (e.Contains("already used to create a Source") || e.Contains("different kind of placeholder")));
    }

    [Fact]
    public void SamePlaceholder_UsedAsFamilyThenReferencedAsPerson_FailsValidation()
    {
        WriteBaseFile();
        // @New1@ is fixed as a Family by the first op's family field, then a
        // later op tries to use the same token as a person reference.
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New1@", "husb": "@I00002@",
                "child": { "xref": "@New2@", "name": "Kid /Test/", "sex": "M" } },
              { "op": "createOrUpdateSpouse", "person": "@New1@",
                "spouse": "@I00004@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@"));
    }

    // -------------------------------------------------------------------
    // Forward references
    // -------------------------------------------------------------------

    [Fact]
    public void PlaceholderReferencedBeforeItsCreatingOp_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@New1@", "text": "too early" },
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@New1@", "name": "New /Wife/", "sex": "F" },
                "family": "@New2@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("not in file"));
    }

    [Fact]
    public void PlaceholderNeverCreatedAnywhereInChangeset_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@NewGhost@", "text": "nothing creates this" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@NewGhost@") && e.Contains("not in file"));
    }

    // -------------------------------------------------------------------
    // Cross-occurrence inline-identity conflicts (person only)
    // -------------------------------------------------------------------

    [Fact]
    public void SamePersonPlaceholder_ConflictingNameAcrossOccurrences_FailsValidation()
    {
        WriteBaseFile();
        // @New1@ is first created as "First /Name/" via createOrUpdateChild,
        // then a later op in the same changeset supplies a different inline
        // name for the same token.
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00002@",
                "child": { "xref": "@New1@", "name": "First /Name/", "sex": "M" } },
              { "op": "createOrUpdateSpouse", "person": "@I00004@",
                "spouse": { "xref": "@New1@", "name": "Different /Name/", "sex": "M" },
                "family": "@New3@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("conflicting name"));
    }

    [Fact]
    public void SamePersonPlaceholder_ConflictingSexAcrossOccurrences_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00002@",
                "child": { "xref": "@New1@", "name": "Same /Name/", "sex": "M" } },
              { "op": "createOrUpdateSpouse", "person": "@I00004@",
                "spouse": { "xref": "@New1@", "name": "Same /Name/", "sex": "F" },
                "family": "@New3@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("conflicting sex"));
    }

    [Fact]
    public void SamePersonPlaceholder_CompatibleRepeatedInlineEvidence_Succeeds()
    {
        WriteBaseFile();
        // Same name and sex supplied twice for the same token: compatible,
        // not a conflict. Both occurrences (the creating child op and the
        // later spouse reference) resolve to one real person, minted once.
        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New2@", "husb": "@I00002@",
                "child": { "xref": "@New1@", "name": "Repeat /Name/", "sex": "M" } },
              { "op": "createOrUpdateSpouse", "person": "@I00004@",
                "spouse": { "xref": "@New1@", "name": "Repeat /Name/", "sex": "M" },
                "family": "@New3@" } ] } ] }
            """);

        Assert.Equal(1, result.Deltas["INDI"]);   // minted exactly once, not twice
        var doc = ReadDoc();
        string person = result.MintedXrefs["@New1@"];
        string childFamily = result.MintedXrefs["@New2@"];
        string spouseFamily = result.MintedXrefs["@New3@"];
        Assert.Equal(person, doc.ByXref[childFamily].ChildrenByTag("CHIL").Single().Value);
        Assert.Equal(person, doc.ByXref[spouseFamily].FirstChild("HUSB")!.Value);
    }

    // -------------------------------------------------------------------
    // The one collision this can't statically rule out
    // -------------------------------------------------------------------

    [Fact]
    public void DocumentAlreadyContainsARealRecordShapedLikeAPlaceholder_FailsLoudly()
    {
        // A foreign import whose own xref literally matches @New<token>@.
        var lines = BaseLines.ToList();
        int insertAt = lines.FindIndex(l => l.StartsWith("0 @F00001@"));
        lines.InsertRange(insertAt, new[]
        {
            "0 @New1@ INDI",
            "1 NAME Foreign /Import/",
            "1 SEX M",
        });
        var document = Ged70Parser.Parse(string.Join("\r\n", lines) + "\r\n");
        var output = new MemoryStream();
        Ged70Formatter.Write(document, output);
        var bytes = output.ToArray();

        var changeset = Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@New1@", "name": "New /Wife/", "sex": "F" },
                "family": "@NewFam1@" } ] } ] }
            """);
        var result = ChangesetApplier.Run(bytes, changeset, [1], dryRun: false);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@New1@") && e.Contains("already contains a real"));
    }

    // -------------------------------------------------------------------
    // Same-partner-same-date marriage conflict (independent of person
    // duplicate detection)
    // -------------------------------------------------------------------

    [Fact]
    public void SecondMarriage_SamePartner_SameExactDate_NewFamily_FailsValidation()
    {
        WriteBaseFile();
        // Allen (@I00001@) and Edna (@I00004@) already share @F00002@ with no
        // MARR; give it an exact date first, then try to create a second,
        // brand-new family between the same two people on that same date.
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@F00002@", "fact": "MARR",
                "value": { "date": "12 JUN 1922" },
                "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } ] } ] }
            """);

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@",
                "family": "@NewF1@",
                "marriage": { "date": "12 JUN 1922",
                              "citation": { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 } } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@F00002@") && e.Contains("same date"));
    }

    [Fact]
    public void SecondMarriage_SamePartner_DifferentDate_Succeeds()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@F00002@", "fact": "MARR",
                "value": { "date": "12 JUN 1922" },
                "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } ] } ] }
            """);

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@",
                "family": "@NewF1@",
                "marriage": { "date": "13 JUN 1930",
                              "citation": { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 } } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("13 JUN 1930", ReadDoc().ByXref[result.MintedXrefs["@NewF1@"]]
            .FirstChild("MARR")!.FirstChild("DATE")!.Value);
    }

    [Fact]
    public void SecondMarriage_SamePartner_SameDate_ButExistingFamilyNamedExplicitly_IsNotACreate_Succeeds()
    {
        // Naming the SAME existing family (not creating a new one) with the
        // same date is just an ordinary idempotent MARR update, not a conflict.
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@F00002@", "fact": "MARR",
                "value": { "date": "12 JUN 1922" },
                "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } ] } ] }
            """);

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@",
                "family": "@F00002@",
                "marriage": { "date": "12 JUN 1922",
                              "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void SecondMarriage_SamePartner_PartialDate_DoesNotTriggerTheRule()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@F00002@", "fact": "MARR",
                "value": { "date": "1922" },
                "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } ] } ] }
            """);

        // Both this new marriage's date AND the existing one are year-only —
        // neither yields an exact full-date identity, so the rule never
        // triggers, even though a human would call these "the same date".
        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@",
                "family": "@NewF1@",
                "marriage": { "date": "1922",
                              "citation": { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 } } } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void SecondMarriage_SamePartner_SameDate_TwoNewFamiliesInOneChangeset_FailsValidation()
    {
        // Neither family exists yet -- both are new-family creations for the
        // same pair within the same changeset, on the same exact date.
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@NewI1@", "name": "First /Wife/", "sex": "F" },
                "family": "@NewF1@",
                "marriage": { "date": "1 JAN 1900",
                              "citation": { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 } } },
              { "op": "createOrUpdateSpouse", "person": "@NewI1@", "spouse": "@I00002@",
                "family": "@NewF2@",
                "marriage": { "date": "1 JAN 1900",
                              "citation": { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 } } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@NewF1@") && e.Contains("same date"));
    }

    // -------------------------------------------------------------------
    // The numeric allocation rule itself, through a realistic document shape
    // -------------------------------------------------------------------

    [Fact]
    public void Minting_PadsToTheWidestExistingSuffix_NotJustFive()
    {
        // Add an unreferenced INDI with a wide, non-standard xref alongside
        // the normal ones, and confirm the next mint respects that width.
        var lines = BaseLines.ToList();
        int insertAt = lines.FindIndex(l => l.StartsWith("0 @F00001@"));
        lines.InsertRange(insertAt, new[]
        {
            "0 @I0000099@ INDI",
            "1 NAME Wide /Xref/",
            "1 SEX M",
        });
        var document = Ged70Parser.Parse(string.Join("\r\n", lines) + "\r\n");
        var output = new MemoryStream();
        Ged70Formatter.Write(document, output);
        var bytes = output.ToArray();

        var changeset = Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@",
                "spouse": { "xref": "@New1@", "name": "New /Wife/", "sex": "F" },
                "family": "@NewFam1@" } ] } ] }
            """);
        var result = ChangesetApplier.Run(bytes, changeset, [1], dryRun: false);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        // Widest existing INDI suffix is 7 digits (0000099, value 99); next
        // value 100, padded to that same width.
        Assert.Equal("@I0000100@", result.MintedXrefs["@New1@"]);
    }
}

namespace GedCore.Tests;

/// <summary>
/// Per-op tests for the simple nouns (Vital, Source, Citation, Note):
/// create / update-logs-replaced-value / no-op / delete paths, fact
/// selectors, and the replacedCitations dispositions.
/// </summary>
public class ApplyOpTests : ApplyTestBase
{
    /// <summary>Attach @S00001@ to Allen's BIRT so it carries a pre-existing citation.</summary>
    private void SeedBirtWithExistingCitation()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 1",
                              "dataText": "born 1928 per old source", "quay": 1 } } ] } ] }
            """);
    }

    // -------------------------------------------------------------------------
    // Vital
    // -------------------------------------------------------------------------

    [Fact]
    public void Vital_Create_InsertsFactBeforeLinks_WithPaddedDateAndCitation()
    {
        WriteBaseFile();

                RunExpectSuccess(ChangesetFixtures.Load("create-or-update-vital"));

        var indi = ReadDoc().ByXref["@I00001@"];
        var deat = indi.FirstChild("DEAT")!;
        Assert.Equal("09 APR 2009", deat.FirstChild("DATE")!.Value);
        Assert.Equal("@S00001@", deat.FirstChild("SOUR")!.Value);
        var tags = indi.Children.Select(c => c.Tag).ToList();
        Assert.True(tags.IndexOf("DEAT") < tags.IndexOf("FAMS"));
    }

    [Fact]
    public void Vital_Update_ReplacesDateAndPlace_LoggingReplacedValues()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
                "citation": { "source": "@S00001@", "page": "p. 1",
                              "dataText": "born April 2, 1928", "quay": 2 } } ] } ] }
            """);

        var birt = ReadDoc().ByXref["@I00001@"].FirstChild("BIRT")!;
        Assert.Equal("02 APR 1928", birt.FirstChild("DATE")!.Value);
        Assert.Equal("Fergus Falls, Minnesota", birt.FirstChild("PLAC")!.Value);
        // the dry-run/apply log is the review artifact: replaced values are named
        Assert.Contains(result.Log, l => l.Contains("'1928' → '02 APR 1928'"));
        Assert.Contains(result.Log, l => l.Contains("'Minnesota' → 'Fergus Falls, Minnesota'"));
    }

    [Fact]
    public void Vital_EqualValue_IsNoOp_OnceCitationIsPresent()
    {
        WriteBaseFile();
        const string json = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "1928", "place": "Minnesota" },
                "citation": { "source": "@S00001@", "page": "p. 1",
                              "dataText": "born 1928", "quay": 2 } } ] } ] }
            """;

        var first = RunExpectSuccess(json);   // value equal, citation newly attached
        Assert.Contains(first.Log, l => l.Contains("cited @S00001@"));
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(json);
        Assert.Contains(second.Log, l => l.Contains("no-op (already matches)"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void Vital_Match_SelectsAmongMultipleSameTagFacts()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00003@", "fact": "CENS",
                "match": { "date": "1940" }, "value": { "place": "Fergus Falls, Minnesota" },
                "citation": { "source": "@S00001@", "page": "ED 26-3",
                              "dataText": "Nellie, Fergus Falls", "quay": 2 } } ] } ] }
            """);

        var census = ReadDoc().ByXref["@I00003@"].ChildrenByTag("CENS").ToList();
        Assert.Equal("Minnesota",
            census.Single(c => c.FirstChild("DATE")!.Value == "1930").FirstChild("PLAC")!.Value);
        Assert.Equal("Fergus Falls, Minnesota",
            census.Single(c => c.FirstChild("DATE")!.Value == "1940").FirstChild("PLAC")!.Value);
    }

    /// <summary>
    /// The 2026-07-13 corruption guard: a no-match createOrUpdateVital on a
    /// repeatable fact (CENS) that already has one instance would overwrite it.
    /// That is refused at validation so a second census can never silently
    /// clobber the first — the dry-run catches it and the file is untouched.
    /// </summary>
    [Fact]
    public void Vital_RepeatableNoMatch_WouldOverwriteExisting_FailsValidation_FileUntouched()
    {
        WriteBaseFile();
        // Harvey has no CENS; give him one (creating the first instance is always fine).
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "CENS",
                "value": { "date": "1900", "place": "Otter Tail Co., Minnesota" },
                "citation": { "source": "@S00001@", "page": "1900 ED 5",
                              "dataText": "Harvey, 1900", "quay": 2 } } ] } ] }
            """);
        byte[] afterFirst = ReadBytes();

        // A second census, no match, different date — would overwrite the 1900 one.
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "CENS",
                "value": { "date": "1910", "place": "Grant Co., Minnesota" },
                "citation": { "source": "@S00001@", "page": "1910 ED 9",
                              "dataText": "Harvey, 1910", "quay": 2 } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("may occur more than once") && e.Contains("mode"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    /// <summary>
    /// mode:"add" is the sanctioned way to add a second instance of a
    /// repeatable fact: the existing 1900 census is left intact and a new 1910
    /// census is created alongside it (what the corrupting no-match op could not do).
    /// </summary>
    [Fact]
    public void Vital_ModeAdd_AddsSecondCensus_LeavingFirstIntact()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "CENS",
                "value": { "date": "1900", "place": "Otter Tail Co., Minnesota" },
                "citation": { "source": "@S00001@", "page": "1900 ED 5",
                              "dataText": "Harvey, 1900", "quay": 2 } } ] } ] }
            """);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "CENS", "mode": "add",
                "value": { "date": "1910", "place": "Grant Co., Minnesota" },
                "citation": { "source": "@S00001@", "page": "1910 ED 9",
                              "dataText": "Harvey, 1910", "quay": 2 } } ] } ] }
            """);

        var census = ReadDoc().ByXref["@I00002@"].ChildrenByTag("CENS").ToList();
        Assert.Equal(2, census.Count);
        Assert.Equal("Otter Tail Co., Minnesota",
            census.Single(c => c.FirstChild("DATE")!.Value == "1900").FirstChild("PLAC")!.Value);
        Assert.Equal("Grant Co., Minnesota",
            census.Single(c => c.FirstChild("DATE")!.Value == "1910").FirstChild("PLAC")!.Value);
    }

    /// <summary>
    /// mode:"add" stays idempotent: re-running the same add finds the identical
    /// instance already present and no-ops rather than duplicating it.
    /// </summary>
    [Fact]
    public void Vital_ModeAdd_ReRun_DoesNotDuplicate()
    {
        WriteBaseFile();
        const string add = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "CENS", "mode": "add",
                "value": { "date": "1910", "place": "Grant Co., Minnesota" },
                "citation": { "source": "@S00001@", "page": "1910 ED 9",
                              "dataText": "Harvey, 1910", "quay": 2 } } ] } ] }
            """;
        RunExpectSuccess(add);
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(add);
        Assert.Contains(second.Log, l => l.Contains("no-op (already matches)"));
        Assert.Equal(afterFirst, ReadBytes());
        Assert.Single(ReadDoc().ByXref["@I00002@"].ChildrenByTag("CENS"));
    }

    /// <summary>mode:"add" cannot be combined with a match selector — the two
    /// intents contradict, and the op is rejected at validation.</summary>
    [Fact]
    public void Vital_ModeAdd_WithMatch_FailsValidation()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00003@", "fact": "CENS", "mode": "add",
                "match": { "date": "1940" }, "value": { "date": "1950", "place": "Iowa" },
                "citation": { "source": "@S00001@", "page": "1950 ED 1",
                              "dataText": "Nellie, 1950", "quay": 2 } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("mode") && e.Contains("match"));
    }

    [Fact]
    public void Vital_AddsPlaceToDateOnlyEvent_LeavingDateUntouched()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "DEAT",
                "value": { "date": "5 OCT 1954" },
                "citation": { "source": "@S00001@", "page": "p. 9",
                              "dataText": "died Oct 5 1954", "quay": 2 } } ] } ] }
            """);

        // the burial-place enrichment: add the missing PLAC, date untouched
        RunExpectSuccess("""
            { "newSources": [ { "xref": "@S00002@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "Find a Grave",
                  "title": "memorial" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "DEAT",
                "value": { "place": "Benton Harbor, Berrien Co., Michigan" },
                "citation": { "source": "@S00002@", "page": "memorial",
                              "dataText": "Burial: Crystal Springs Cemetery", "quay": 3 } } ] } ] }
            """);

        var deat = ReadDoc().ByXref["@I00002@"].FirstChild("DEAT")!;
        Assert.Equal("05 OCT 1954", deat.ChildrenByTag("DATE").Single().Value);
        Assert.Equal("Benton Harbor, Berrien Co., Michigan",
            deat.ChildrenByTag("PLAC").Single().Value);
        // event detail (PLAC) must precede the citations (SOUR)
        Assert.True(deat.Children.FindIndex(c => c.Tag == "PLAC")
                    < deat.Children.FindIndex(c => c.Tag == "SOUR"));
    }

    [Fact]
    public void Vital_TextPayload_UpdatesName_WithSubstructureFirst()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
                "value": "Albin H. /Test/",
                "substructures": [ { "tag": "NICK", "value": "Babe" } ],
                "citation": { "source": "@S00001@", "page": "p. 1",
                              "dataText": "Albin H. Test", "quay": 2 } } ] } ] }
            """);

        var name = ReadDoc().ByXref["@I00001@"].FirstChild("NAME")!;
        Assert.Equal("Albin H. /Test/", name.Value);
        Assert.Equal("NICK", name.Children[0].Tag);
        Assert.NotNull(name.FirstChild("SOUR"));
    }

    [Fact]
    public void Vital_ReplacedCitationsDefaultKeep_PreservesPreexistingCitation()
    {
        SeedBirtWithExistingCitation();

        RunExpectSuccess("""
            { "newSources": [ { "xref": "@S00002@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "New Source",
                  "title": "New source" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
                "citation": { "source": "@S00002@", "page": "p. 1",
                              "dataText": "born April 2, 1928", "quay": 2 } } ] } ] }
            """);

        var sources = ReadDoc().ByXref["@I00001@"].FirstChild("BIRT")!
            .ChildrenByTag("SOUR").Select(s => s.Value).ToList();
        Assert.Contains("@S00001@", sources);   // kept (default)
        Assert.Contains("@S00002@", sources);   // new citation attached
    }

    [Fact]
    public void Vital_ReplacedCitationsDrop_RemovesPreexistingCitation()
    {
        SeedBirtWithExistingCitation();

        RunExpectSuccess("""
            { "newSources": [ { "xref": "@S00002@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "New Source",
                  "title": "New source" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
                "replacedCitations": "drop",
                "citation": { "source": "@S00002@", "page": "p. 1",
                              "dataText": "born April 2, 1928", "quay": 2 } } ] } ] }
            """);

        var indi = ReadDoc().ByXref["@I00001@"];
        var sources = indi.FirstChild("BIRT")!.ChildrenByTag("SOUR").Select(s => s.Value).ToList();
        Assert.DoesNotContain("@S00001@", sources);
        Assert.Contains("@S00002@", sources);
        Assert.DoesNotContain(indi.Children, c => c.Tag == "NOTE");   // no note created
    }

    [Fact]
    public void Vital_ReplacedCitationsMoveToNote_CreatesMinimalNote()
    {
        SeedBirtWithExistingCitation();

        RunExpectSuccess("""
            { "newSources": [ { "xref": "@S00002@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "New Source",
                  "title": "New source" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
                "replacedCitations": "moveToNote",
                "citation": { "source": "@S00002@", "page": "p. 1",
                              "dataText": "born April 2, 1928", "quay": 2 } } ] } ] }
            """);

        var indi = ReadDoc().ByXref["@I00001@"];
        var birtSources = indi.FirstChild("BIRT")!.ChildrenByTag("SOUR").Select(s => s.Value).ToList();
        Assert.DoesNotContain("@S00001@", birtSources);
        Assert.Contains("@S00002@", birtSources);

        var note = indi.ChildrenByTag("NOTE").Single();
        var noteSour = note.FirstChild("SOUR")!;
        Assert.Equal("@S00001@", noteSour.Value);
        Assert.Equal("p. 1", noteSour.FirstChild("PAGE")!.Value);
        Assert.Equal("born 1928 per old source",
            noteSour.FirstChild("DATA")!.FirstChild("TEXT")!.Value);
    }

    [Fact]
    public void Vital_ReplacedCitationsMoveToNote_ReusesNoteFromSameItem_NotUnrelatedOnes()
    {
        SeedBirtWithExistingCitation();
        // an unrelated pre-existing note must never receive the moved citation
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Unrelated biography note." } ] } ] }
            """);

        RunExpectSuccess("""
            { "newSources": [ { "xref": "@S00002@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "New Source",
                  "title": "New source" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Birth date refined; prior citation retained below." },
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
                "replacedCitations": "moveToNote",
                "citation": { "source": "@S00002@", "page": "p. 1",
                              "dataText": "born April 2, 1928", "quay": 2 } } ] } ] }
            """);

        var notes = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").ToList();
        Assert.Equal(2, notes.Count);
        var unrelated = notes.Single(n => n.Value == "Unrelated biography note.");
        Assert.Null(unrelated.FirstChild("SOUR"));
        var itemNote = notes.Single(n => n.Value == "Birth date refined; prior citation retained below.");
        Assert.Equal("@S00001@", itemNote.FirstChild("SOUR")!.Value);
    }

    [Fact]
    public void Note_WithCitation_AttachesSourceUnderNote_AndIsIdempotent()
    {
        WriteBaseFile();
        const string changeset = """
            { "newSources": [ { "xref": "@S00002@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "State",
                  "title": "Draft card" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Adopted his maternal surname in adulthood.",
                "citation": { "source": "@S00002@", "page": "1942 draft",
                              "dataText": "Allen also known as Allen Other", "quay": 3 } } ] } ] }
            """;
        RunExpectSuccess(changeset);

        var note = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE")
            .Single(n => n.Value == "Adopted his maternal surname in adulthood.");
        var sour = note.FirstChild("SOUR");
        Assert.NotNull(sour);
        Assert.Equal("@S00002@", sour!.Value);
        Assert.Equal("1942 draft", sour.FirstChild("PAGE")!.Value);
        Assert.Equal("3", sour.FirstChild("QUAY")!.Value);

        // Re-applying the same changeset is a no-op: byte-identical file, one SOUR.
        var before = ReadBytes();
        RunExpectSuccess(changeset);
        Assert.Equal(before, ReadBytes());
        var note2 = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE")
            .Single(n => n.Value == "Adopted his maternal surname in adulthood.");
        Assert.Single(note2.ChildrenByTag("SOUR"));
    }

    [Fact]
    public void Note_MultilinePlainText_UsesContAndUpdatesByFullText()
    {
        WriteBaseFile();
        const string originalText = "First paragraph.\n\nSecond paragraph.";
        const string revisedText = "First paragraph, revised.\n\nSecond paragraph.";

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "First paragraph.\n\nSecond paragraph.",
                "citation": { "source": "@S00001@", "page": "p. 7",
                              "dataText": "biographical detail", "quay": 2 } } ] } ] }
            """);

        var note = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").Single();
        Assert.Equal(originalText, note.FullValue());
        Assert.Equal(["", "Second paragraph."], note.ChildrenByTag("CONT").Select(cont => cont.Value));
        Assert.Equal("@S00001@", note.FirstChild("SOUR")!.Value);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "match": "First paragraph.\n\nSecond paragraph.",
                "text": "First paragraph, revised.\n\nSecond paragraph." } ] } ] }
            """);

        var updated = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").Single();
        Assert.Equal(revisedText, updated.FullValue());
        Assert.Equal("@S00001@", updated.FirstChild("SOUR")!.Value);
    }

    [Fact]
    public void Note_HtmlMime_IsStoredAndCanBeChangedWithoutLosingCitations()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "mime": "text/html",
                "text": "Named after <i>The Odyssey</i>.",
                "citation": { "source": "@S00001@", "page": "p. 7", "quay": 2 } } ] } ] }
            """);

        var note = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").Single();
        Assert.Equal("text/html", note.FirstChild("MIME")!.Value);
        Assert.Equal("@S00001@", note.FirstChild("SOUR")!.Value);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "mime": "text/plain",
                "text": "Named after <i>The Odyssey</i>." } ] } ] }
            """);

        note = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").Single();
        Assert.Null(note.FirstChild("MIME"));
        Assert.Equal("@S00001@", note.FirstChild("SOUR")!.Value);
    }

    [Fact]
    public void Note_UnsupportedMime_IsRejected()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "mime": "text/markdown",
                "text": "A note." } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("mime must be text/plain or text/html"));
    }

    [Fact]
    public void DeleteVital_RemovesFactAndItsCitations()
    {
        SeedBirtWithExistingCitation();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteVital", "record": "@I00001@", "fact": "BIRT" } ] } ] }
            """);

        var indi = ReadDoc().ByXref["@I00001@"];
        Assert.Null(indi.FirstChild("BIRT"));
        Assert.DoesNotContain(indi.Children, c => c.Tag == "NOTE");
        Assert.Contains(result.Log, l => l.Contains("citation(s) dropped"));
    }

    [Fact]
    public void DeleteVital_MoveToNote_PreservesCitationsOnANote()
    {
        SeedBirtWithExistingCitation();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteVital", "record": "@I00001@", "fact": "BIRT",
                "deletedCitations": "moveToNote" } ] } ] }
            """);

        var indi = ReadDoc().ByXref["@I00001@"];
        Assert.Null(indi.FirstChild("BIRT"));
        var note = indi.ChildrenByTag("NOTE").Single();
        Assert.Equal("@S00001@", note.FirstChild("SOUR")!.Value);
    }

    [Fact]
    public void DeleteVital_AbsentFact_IsNoOp()
    {
        byte[] original = WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteVital", "record": "@I00001@", "fact": "DEAT" } ] } ] }
            """);

        Assert.Contains(result.Log, l => l.Contains("no-op (absent)"));
        Assert.Equal(original, ReadBytes());
    }

    // -------------------------------------------------------------------------
    // Source
    // -------------------------------------------------------------------------

    [Fact]
    public void Source_Update_ChangesProvidedFields_LoggingReplacedValues()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSource", "xref": "@S00001@",
                "auth": "County Clerk", "title": "Renamed source" } ] } ] }
            """);

        var sour = ReadDoc().ByXref["@S00001@"];
        Assert.Equal("County Clerk", sour.FirstChild("AUTH")!.Value);
        Assert.Equal("Renamed source", sour.FirstChild("TITL")!.Value);
        Assert.Contains(result.Log, l => l.Contains("'Existing source' → 'Renamed source'"));

        // identical re-run is a no-op
        var second = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSource", "xref": "@S00001@",
                "auth": "County Clerk", "title": "Renamed source" } ] } ] }
            """);
        Assert.Contains(second.Log, l => l.Contains("no-op"));
    }

    [Fact]
    public void DeleteSource_Unreferenced_RemovesRecord_WithNegativeDelta()
    {
        WriteBaseFile();

                var result = RunExpectSuccess(ChangesetFixtures.Load("delete-source"));

        Assert.Equal(-1, result.Deltas["SOUR"]);
        Assert.False(ReadDoc().ByXref.ContainsKey("@S00001@"));
    }

    [Fact]
    public void DeleteSource_StillCited_FailsValidation()
    {
        SeedBirtWithExistingCitation();
        byte[] beforeAttempt = ReadBytes();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteSource", "xref": "@S00001@" } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("still cited"));
        Assert.Equal(beforeAttempt, ReadBytes());
    }

    // -------------------------------------------------------------------------
    // Citation
    // -------------------------------------------------------------------------

    [Fact]
    public void Citation_Create_AttachesFullCitationToExistingFact()
    {
        WriteBaseFile();

                RunExpectSuccess(ChangesetFixtures.Load("create-or-update-citation"));

        var birt = ReadDoc().ByXref["@I00001@"].FirstChild("BIRT")!;
        var sour = birt.FirstChild("SOUR")!;
        Assert.Equal("@S00001@", sour.Value);
        Assert.Equal("p. 2", sour.FirstChild("PAGE")!.Value);
        Assert.Equal("2", sour.FirstChild("QUAY")!.Value);
        Assert.Equal("born 1928", sour.FirstChild("DATA")!.FirstChild("TEXT")!.Value);
        Assert.Equal("1928", birt.FirstChild("DATE")!.Value);   // fact untouched
    }

    [Fact]
    public void Citation_IdenticalRerun_IsNoOp_NotAnError()
    {
        WriteBaseFile();
        const string json = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "born 1928", "quay": 2 } } ] } ] }
            """;

        RunExpectSuccess(json);
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(json);   // dialect v1 made this a hard error
        Assert.Contains(second.Log, l => l.Contains("no-op (already cited identically)"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void Citation_SameSourceDifferentLocator_UpdatesInPlace()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "born 1928", "quay": 2 } } ] } ] }
            """);

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 3, corrected",
                              "dataText": "born 1928", "quay": 2 } } ] } ] }
            """);

        var citations = ReadDoc().ByXref["@I00001@"].FirstChild("BIRT")!
            .ChildrenByTag("SOUR").ToList();
        Assert.Single(citations);   // updated in place, not duplicated
        Assert.Equal("p. 3, corrected", citations[0].FirstChild("PAGE")!.Value);
        Assert.Contains(result.Log, l => l.Contains("'p. 2' → 'p. 3, corrected'"));
    }

    [Fact]
    public void DeleteCitation_RemovesOneSource_AbsentIsNoOp()
    {
        SeedBirtWithExistingCitation();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT",
                "source": "@S00001@" } ] } ] }
            """);
        Assert.Empty(ReadDoc().ByXref["@I00001@"].FirstChild("BIRT")!.ChildrenByTag("SOUR"));

        var second = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT",
                "source": "@S00001@" } ] } ] }
            """);
        Assert.Contains(second.Log, l => l.Contains("no-op"));
    }

    /// <summary>
    /// A dataText carrying a raw line break serializes to an unparseable line.
    /// It must be rejected at validation (dry-run) with a clear message, not
    /// discovered in the reparse-verify step after a wasted apply attempt.
    /// </summary>
    [Fact]
    public void Citation_MultiLineDataText_FailsValidation_FileUntouched()
    {
        byte[] original = WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "line one\nline two", "quay": 2 } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("dataText") && e.Contains("line break"));
        Assert.Equal(original, ReadBytes());
    }

    /// <summary>A raw line break in the PAGE locator is rejected the same way.</summary>
    [Fact]
    public void Citation_MultiLinePage_FailsValidation()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2\r\np. 3",
                              "dataText": "born 1928", "quay": 2 } } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("page") && e.Contains("line break"));
    }

    /// <summary>
    /// Two ops in one changeset that cite the same source on the same fact with
    /// conflicting dataText resolve to the same SOUR node (one citation per
    /// source per structure). The second must not silently overwrite the first;
    /// the run fails cleanly and the file is untouched.
    /// </summary>
    [Fact]
    public void Citation_ConflictingDataTextInOneChangeset_FailsCleanly_FileUntouched()
    {
        byte[] original = WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "father: born 1928", "quay": 2 } },
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "mother: born 1929", "quay": 2 } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e =>
            e.Contains("apply-time invariant violated") && e.Contains("DATA.TEXT"));
        Assert.Equal(original, ReadBytes());
    }

    /// <summary>
    /// The same source cited on one fact with identical text by two ops is a
    /// no-op on the second, not a conflict — the guard only rejects differing
    /// values.
    /// </summary>
    [Fact]
    public void Citation_SameDataTextTwiceInOneChangeset_IsNotAConflict()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "born 1928", "quay": 2 } },
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 2",
                              "dataText": "born 1928", "quay": 2 } } ] } ] }
            """);

        Assert.Single(ReadDoc().ByXref["@I00001@"].FirstChild("BIRT")!.ChildrenByTag("SOUR"));
    }

    // -------------------------------------------------------------------------
    // Note
    // -------------------------------------------------------------------------

    [Fact]
    public void Note_Create_MatchRewrite_ThenIdenticalNoOp()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Original analysis." } ] } ] }
            """);
        Assert.Equal("Original analysis.",
            ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").Single().Value);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Revised analysis.", "match": "Original analysis." } ] } ] }
            """);
        Assert.Equal("Revised analysis.",
            ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").Single().Value);

        // re-run of the rewrite: match misses but the text is present → no-op
        var third = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "Revised analysis.", "match": "Original analysis." } ] } ] }
            """);
        Assert.Contains(third.Log, l => l.Contains("no-op (already present)"));
    }

    [Fact]
    public void DeleteNote_ByExactText_AbsentIsNoOp()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "text": "Disposable note." } ] } ] }
            """);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteNote", "record": "@I00001@", "text": "Disposable note." } ] } ] }
            """);
        Assert.Empty(ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE"));

        var second = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteNote", "record": "@I00001@", "text": "Disposable note." } ] } ] }
            """);
        Assert.Contains(second.Log, l => l.Contains("no-op"));
    }
}

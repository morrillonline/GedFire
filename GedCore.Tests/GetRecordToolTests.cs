using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class GetRecordToolTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-getrecord-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // -------------------------------------------------------------------
    // Fixture: one subject (I1) exercising every field this tool maps —
    // every event type, multiple citations (one dangling, proving it
    // contributes no entry), media with and without a crop, multiple files,
    // a note with its own citation, a restriction, two marriages (one
    // childless with no MARR tag), a same-named parent, and a standalone
    // wife-less family (F4) for the family-record "wife absent" case.
    // -------------------------------------------------------------------

    const string RecordGed = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @S1@ SOUR
        1 AUTH Jane Doe
        1 TITL Vital Records of Somewhere
        1 PUBL Some Press, 1900
        1 NOTE Vital Records of Somewhere, p. 12
        2 CONT SHORTCITATION: VR Somewhere|NOCITATION: TRUE|
        2 CONT .
        0 @M1@ OBJE
        1 FILE photos/birth-cert.jpg
        2 FORM image/jpeg
        1 TITL Birth certificate
        0 @M2@ OBJE
        1 FILE photos/portrait-1.jpg
        2 FORM image/jpeg
        1 FILE photos/portrait-2.jpg
        2 FORM image/jpeg
        2 TITL Second scan
        1 TITL Family portrait
        0 @I1@ INDI
        1 NAME Abraham /Morrill/
        2 SOUR @S1@
        1 TITL Capt.
        1 SEX M
        1 BIRT
        2 DATE 14 NOV 1652
        2 PLAC Salisbury, Massachusetts
        2 SOUR @S1@
        2 OBJE @M1@
        3 CROP
        4 TOP 10
        4 LEFT 20
        4 HEIGHT 100
        4 WIDTH 200
        1 DEAT
        2 DATE 1698
        2 PLAC Salisbury, Massachusetts
        2 SOUR @S999@
        1 WILL
        2 DATE 1697
        1 PROB
        2 DATE 1698
        1 CENS
        2 DATE 1690
        2 PLAC Salisbury, Massachusetts
        1 CENS
        2 DATE 1695
        1 NOTE A locally prominent figure.
        2 SOUR @S1@
        1 RESN CONFIDENTIAL
        1 OBJE @M2@
        1 FAMC @F1@
        1 FAMS @F2@
        1 FAMS @F3@
        0 @I2@ INDI
        1 NAME Abraham /Morrill/
        1 SEX M
        1 FAMS @F1@
        1 FAMS @F4@
        0 @I3@ INDI
        1 NAME Sarah /Clements/
        1 SEX F
        1 FAMS @F1@
        0 @I4@ INDI
        1 NAME Sarah /Bradbury/
        1 SEX F
        1 FAMS @F2@
        0 @I5@ INDI
        1 NAME Abraham /Morrill/
        1 SEX M
        1 BIRT
        2 DATE 1671
        1 FAMC @F2@
        0 @I6@ INDI
        1 NAME Second /Wife/
        1 SEX F
        1 FAMS @F3@
        0 @I7@ INDI
        1 NAME No /Sex/
        0 @F1@ FAM
        1 HUSB @I2@
        1 WIFE @I3@
        1 CHIL @I1@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I4@
        1 MARR
        2 DATE 1675
        2 SOUR @S1@
        1 CHIL @I5@
        0 @F3@ FAM
        1 HUSB @I1@
        1 WIFE @I6@
        0 @F4@ FAM
        1 HUSB @I2@
        0 TRLR

        """;

    // Real files matching RecordGed's media references, so the happy-path
    // tests exercise actual path resolution rather than asserting against
    // paths nothing backs.
    string MediaDir()
    {
        string mediaDir = Path.Combine(_dir, "media");
        Directory.CreateDirectory(Path.Combine(mediaDir, "photos"));
        File.WriteAllText(Path.Combine(mediaDir, "photos", "birth-cert.jpg"), "fake-jpeg-bytes");
        File.WriteAllText(Path.Combine(mediaDir, "photos", "portrait-1.jpg"), "fake-jpeg-bytes");
        File.WriteAllText(Path.Combine(mediaDir, "photos", "portrait-2.jpg"), "fake-jpeg-bytes");
        return mediaDir;
    }

    GetRecordTool ToolOver(string gedText, string? mediaDir = null)
    {
        string path = Path.Combine(_dir, Guid.NewGuid() + ".ged");
        File.WriteAllText(path, gedText);
        var doc = GedCore.GedReader.ReadFile(path);
        var model = GedFire.Gen.ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        return new GetRecordTool(session, new ToolGate(), mediaDir ?? MediaDir());
    }

    static JsonElement StructuredContent(CallToolResult result) => result.StructuredContent!.Value;

    static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    // -------------------------------------------------------------------
    // Person record
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Person_MapsIdentityTitleAndSex()
    {
        var tool = ToolOver(RecordGed);
        var result = await tool.HandleAsync("@I1@", CancellationToken.None);

        Assert.False(result.IsError);
        var root = StructuredContent(result);
        Assert.Equal("person", root.GetProperty("recordType").GetString());
        Assert.Equal("@I1@", root.GetProperty("xref").GetString());
        Assert.Equal("Abraham Morrill", root.GetProperty("name").GetString());
        Assert.Equal("Capt.", root.GetProperty("title").GetString());
        Assert.Equal("M", root.GetProperty("sex").GetString());
    }

    [Fact]
    public async Task HandleAsync_Person_BirthEvent_HasDateSourceAndCroppedMedia()
    {
        string mediaDir = MediaDir();
        var tool = ToolOver(RecordGed, mediaDir);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        var birth = root.GetProperty("birth");
        Assert.Equal("14 NOV 1652", birth.GetProperty("date").GetString());
        Assert.Equal(1652, birth.GetProperty("year").GetInt32());
        Assert.Equal("Salisbury, Massachusetts", birth.GetProperty("place").GetString());
        Assert.Equal(["@S1@"], birth.GetProperty("sources").EnumerateArray().Select(e => e.GetString()));

        var media = Assert.Single(birth.GetProperty("media").EnumerateArray());
        Assert.Equal("@M1@", media.GetProperty("xref").GetString());
        Assert.Equal("Birth certificate", media.GetProperty("title").GetString());
        var crop = media.GetProperty("crop");
        Assert.Equal(10, crop.GetProperty("top").GetInt32());
        Assert.Equal(20, crop.GetProperty("left").GetInt32());
        Assert.Equal(100, crop.GetProperty("height").GetInt32());
        Assert.Equal(200, crop.GetProperty("width").GetInt32());
        var file = Assert.Single(media.GetProperty("files").EnumerateArray());
        // A real file backs this path, so it resolves to the absolute path
        // rather than staying as the raw GEDCOM-relative payload.
        Assert.Equal(Path.Combine(mediaDir, "photos", "birth-cert.jpg"), file.GetProperty("path").GetString());
        Assert.True(file.GetProperty("resolved").GetBoolean());
        Assert.Equal("image/jpeg", file.GetProperty("mediaType").GetString());
    }

    [Fact]
    public async Task HandleAsync_Person_DanglingCitation_ContributesNoSourceEntry()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        // DEAT cites @S999@, which no @S999@ SOUR record backs -- there is
        // no id to give, so the sources array is empty, not an error.
        var death = root.GetProperty("death");
        Assert.Equal("1698", death.GetProperty("date").GetString());
        Assert.Empty(death.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task HandleAsync_Person_WillAndProbate_HaveNoPlace()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        var will = root.GetProperty("will");
        Assert.Equal("1697", will.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, will.GetProperty("place").ValueKind);

        var probate = root.GetProperty("probate");
        Assert.Equal("1698", probate.GetProperty("date").GetString());
    }

    [Fact]
    public async Task HandleAsync_Person_CensusIsAList()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        var census = root.GetProperty("census").EnumerateArray().ToList();
        Assert.Equal(2, census.Count);
        Assert.Equal("1690", census[0].GetProperty("date").GetString());
        Assert.Equal("Salisbury, Massachusetts", census[0].GetProperty("place").GetString());
        Assert.Equal("1695", census[1].GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, census[1].GetProperty("place").ValueKind);
    }

    [Fact]
    public async Task HandleAsync_Person_NameSourcesAndNotesWithTheirOwnCitations()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        Assert.Equal(["@S1@"], root.GetProperty("nameSources").EnumerateArray().Select(e => e.GetString()));

        var note = Assert.Single(root.GetProperty("notes").EnumerateArray());
        Assert.Equal("A locally prominent figure.", note.GetProperty("text").GetString());
        Assert.Equal(["@S1@"], note.GetProperty("sources").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task HandleAsync_Person_RestrictionPassedThroughVerbatim()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));
        Assert.Equal("CONFIDENTIAL", root.GetProperty("restriction").GetString());
    }

    [Fact]
    public async Task HandleAsync_Person_TopLevelMedia_MultipleFilesNoCrop()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        var media = Assert.Single(root.GetProperty("media").EnumerateArray());
        Assert.Equal("@M2@", media.GetProperty("xref").GetString());
        Assert.Equal("Family portrait", media.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, media.GetProperty("crop").ValueKind);

        var files = media.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        Assert.Equal(JsonValueKind.Null, files[0].GetProperty("title").ValueKind);
        Assert.True(files[0].GetProperty("resolved").GetBoolean());
        Assert.Equal("Second scan", files[1].GetProperty("title").GetString());
    }

    // -------------------------------------------------------------------
    // Media path resolution
    // -------------------------------------------------------------------

    const string OneMediaFileGed = """
        0 @M1@ OBJE
        1 FILE {0}
        2 FORM image/jpeg
        0 @I1@ INDI
        1 NAME Solo /Person/
        1 SEX M
        1 OBJE @M1@
        """;

    [Fact]
    public async Task HandleAsync_Media_MissingFile_NotResolved_KeepsRawPath()
    {
        string mediaDir = Path.Combine(_dir, "empty-media");
        Directory.CreateDirectory(mediaDir);
        var tool = ToolOver(string.Format(OneMediaFileGed, "does-not-exist.jpg"), mediaDir);

        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));
        var file = Assert.Single(root.GetProperty("media").EnumerateArray().Single().GetProperty("files").EnumerateArray());

        Assert.False(file.GetProperty("resolved").GetBoolean());
        Assert.Equal("does-not-exist.jpg", file.GetProperty("path").GetString());
    }

    [Fact]
    public async Task HandleAsync_Media_PathEscapingMediaDir_NotResolved()
    {
        string mediaDir = Path.Combine(_dir, "escape-media");
        Directory.CreateDirectory(mediaDir);
        // A real file exists, but outside mediaDir -- reachable only by
        // escaping it, the same rejection SiteGenerator.ResolveMediaSrc applies.
        File.WriteAllText(Path.Combine(_dir, "secret.jpg"), "fake-jpeg-bytes");
        var tool = ToolOver(string.Format(OneMediaFileGed, "../secret.jpg"), mediaDir);

        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));
        var file = Assert.Single(root.GetProperty("media").EnumerateArray().Single().GetProperty("files").EnumerateArray());

        Assert.False(file.GetProperty("resolved").GetBoolean());
        Assert.Equal("../secret.jpg", file.GetProperty("path").GetString());
    }

    [Fact]
    public async Task HandleAsync_Media_AbsoluteUrl_PassesThroughAsResolved_NeverFetched()
    {
        string mediaDir = Path.Combine(_dir, "url-media");
        Directory.CreateDirectory(mediaDir);
        var tool = ToolOver(string.Format(OneMediaFileGed, "https://example.com/photo.jpg"), mediaDir);

        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));
        var file = Assert.Single(root.GetProperty("media").EnumerateArray().Single().GetProperty("files").EnumerateArray());

        Assert.True(file.GetProperty("resolved").GetBoolean());
        Assert.Equal("https://example.com/photo.jpg", file.GetProperty("path").GetString());
    }

    [Fact]
    public async Task HandleAsync_Media_RelativePathResolvesToAbsolutePathUnderMediaDir()
    {
        string mediaDir = Path.Combine(_dir, "resolve-media");
        Directory.CreateDirectory(mediaDir);
        File.WriteAllText(Path.Combine(mediaDir, "photo.jpg"), "fake-jpeg-bytes");
        var tool = ToolOver(string.Format(OneMediaFileGed, "photo.jpg"), mediaDir);

        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));
        var file = Assert.Single(root.GetProperty("media").EnumerateArray().Single().GetProperty("files").EnumerateArray());

        Assert.True(file.GetProperty("resolved").GetBoolean());
        Assert.Equal(Path.Combine(mediaDir, "photo.jpg"), file.GetProperty("path").GetString());
    }

    [Fact]
    public void Constructor_RejectsBlankMediaDir()
    {
        var snapshot = new DocumentSnapshot(
            MatchTestModels.Build("0 @I1@ INDI\n1 NAME X /Y/\n"), "7.0", DateTime.UtcNow, 1);
        var session = new DocumentSession(Path.Combine(_dir, "x.ged"), snapshot);
        Assert.Throws<ArgumentException>(() => new GetRecordTool(session, new ToolGate(), ""));
    }

    [Fact]
    public async Task HandleAsync_Person_FamilyAsChild_IncludesBothParentNames()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        var familyAsChild = root.GetProperty("familyAsChild");
        Assert.Equal("@F1@", familyAsChild.GetProperty("xref").GetString());
        Assert.Equal("Abraham Morrill", familyAsChild.GetProperty("fatherName").GetString());
        Assert.Equal("Sarah Clements", familyAsChild.GetProperty("motherName").GetString());
    }

    [Fact]
    public async Task HandleAsync_Person_FamiliesAsSpouse_IncludesChildrenAndSurvivesAChildlessMarriage()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I1@", CancellationToken.None));

        var families = root.GetProperty("familiesAsSpouse").EnumerateArray().ToList();
        Assert.Equal(2, families.Count);

        var withChild = families.Single(f => f.GetProperty("xref").GetString() == "@F2@");
        Assert.Equal("Sarah Bradbury", withChild.GetProperty("spouseName").GetString());
        Assert.Equal("1675", withChild.GetProperty("marriage").GetProperty("date").GetString());
        Assert.Equal(["@S1@"], withChild.GetProperty("marriage").GetProperty("sources").EnumerateArray().Select(e => e.GetString()));
        var child = Assert.Single(withChild.GetProperty("children").EnumerateArray());
        Assert.Equal("@I5@", child.GetProperty("xref").GetString());
        Assert.Equal("Abraham Morrill", child.GetProperty("name").GetString());
        Assert.Equal(1671, child.GetProperty("birthYear").GetInt32());

        // Childless marriage, no MARR tag at all: still present with its
        // own xref and spouse name, marriage null, children empty.
        var childless = families.Single(f => f.GetProperty("xref").GetString() == "@F3@");
        Assert.Equal("Second Wife", childless.GetProperty("spouseName").GetString());
        Assert.Equal(JsonValueKind.Null, childless.GetProperty("marriage").ValueKind);
        Assert.Empty(childless.GetProperty("children").EnumerateArray());
    }

    [Fact]
    public async Task HandleAsync_Person_NoFamChild_FamilyAsChildIsNull()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I2@", CancellationToken.None));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("familyAsChild").ValueKind);
    }

    [Fact]
    public async Task HandleAsync_Person_SexUnrecorded_IsNull()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I7@", CancellationToken.None));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sex").ValueKind);
        Assert.Equal("No Sex", root.GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleAsync_Person_NoEventsMediaOrNotes_MapsNullsAndEmptyArrays()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@I7@", CancellationToken.None));

        Assert.Equal(JsonValueKind.Null, root.GetProperty("birth").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("death").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("will").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("probate").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("title").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("restriction").ValueKind);
        Assert.Empty(root.GetProperty("census").EnumerateArray());
        Assert.Empty(root.GetProperty("nameSources").EnumerateArray());
        Assert.Empty(root.GetProperty("notes").EnumerateArray());
        Assert.Empty(root.GetProperty("media").EnumerateArray());
        Assert.Empty(root.GetProperty("familiesAsSpouse").EnumerateArray());
    }

    // -------------------------------------------------------------------
    // Family record
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Family_MapsHusbandWifeMarriageAndChildren()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@F2@", CancellationToken.None));

        Assert.Equal("family", root.GetProperty("recordType").GetString());
        Assert.Equal("@F2@", root.GetProperty("xref").GetString());
        Assert.Equal("@I1@", root.GetProperty("husband").GetProperty("xref").GetString());
        Assert.Equal("Abraham Morrill", root.GetProperty("husband").GetProperty("name").GetString());
        Assert.Equal("@I4@", root.GetProperty("wife").GetProperty("xref").GetString());
        Assert.Equal("1675", root.GetProperty("marriage").GetProperty("date").GetString());
        var child = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal("@I5@", child.GetProperty("xref").GetString());
        Assert.Equal(1671, child.GetProperty("birthYear").GetInt32());
    }

    [Fact]
    public async Task HandleAsync_Family_NoMarriageTag_MarriageIsNull()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@F3@", CancellationToken.None));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("marriage").ValueKind);
        Assert.Empty(root.GetProperty("children").EnumerateArray());
    }

    [Fact]
    public async Task HandleAsync_Family_NoWife_WifeIsNull()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@F4@", CancellationToken.None));
        Assert.Equal("@I2@", root.GetProperty("husband").GetProperty("xref").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("wife").ValueKind);
    }

    // -------------------------------------------------------------------
    // Source record
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Source_MapsBibliographicFieldsAndReusesFtmNoteParsing()
    {
        var tool = ToolOver(RecordGed);
        var root = StructuredContent(await tool.HandleAsync("@S1@", CancellationToken.None));

        Assert.Equal("source", root.GetProperty("recordType").GetString());
        Assert.Equal("@S1@", root.GetProperty("xref").GetString());
        Assert.Equal("Jane Doe", root.GetProperty("author").GetString());
        Assert.Equal("Vital Records of Somewhere", root.GetProperty("title").GetString());
        Assert.Equal("Some Press, 1900", root.GetProperty("publication").GetString());

        // The raw NOTE payload contains an FTM directive line
        // ("SHORTCITATION: ...|NOCITATION: TRUE|") and a "." terminator;
        // reusing FtmCitationText.ParseSourceNote (not the raw text) is
        // what strips both, leaving only the bibliographic sentence.
        Assert.Equal("Vital Records of Somewhere, p. 12", root.GetProperty("note").GetString());
    }

    // -------------------------------------------------------------------
    // Not found / errors
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("@I404@")]
    [InlineData("not-an-xref-at-all")]
    public async Task HandleAsync_UnresolvableXref_ReturnsNotFoundRatherThanError(string xref)
    {
        var tool = ToolOver(RecordGed);
        var result = await tool.HandleAsync(xref, CancellationToken.None);

        Assert.False(result.IsError);
        var root = StructuredContent(result);
        Assert.Equal("not_found", root.GetProperty("recordType").GetString());
        Assert.Equal(xref, root.GetProperty("xref").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_BlankXref_ReturnsIsError(string xref)
    {
        var tool = ToolOver(RecordGed);
        var result = await tool.HandleAsync(xref, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains("blank", TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_ReloadFailure_ReturnsIsErrorWithExceptionDetails()
    {
        string path = Path.Combine(_dir, "gone.ged");
        File.WriteAllText(path, RecordGed);
        var doc = GedCore.GedReader.ReadFile(path);
        var model = GedFire.Gen.ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        var tool = new GetRecordTool(session, new ToolGate(), _dir);
        File.Delete(path);

        var result = await tool.HandleAsync("@I1@", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(nameof(DocumentReloadException), TextOf(result));
        Assert.Contains(path, TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_RateLimitRejection_ReturnsIsErrorRatherThanThrowing()
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string path = Path.Combine(_dir, "gate.ged");
        File.WriteAllText(path, RecordGed);
        var doc = GedCore.GedReader.ReadFile(path);
        var model = GedFire.Gen.ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        var gate = new ToolGate(() => now);
        var tool = new GetRecordTool(session, gate, _dir);

        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await tool.HandleAsync("@I1@", CancellationToken.None);

        var result = await tool.HandleAsync("@I1@", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(nameof(ToolRateLimitExceededException), TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_AlreadyCancelledToken_PropagatesRatherThanReturningIsError()
    {
        var tool = ToolOver(RecordGed);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.HandleAsync("@I1@", cts.Token));
    }

    // -------------------------------------------------------------------
    // Tool metadata
    // -------------------------------------------------------------------

    [Fact]
    public void ToMcpServerTool_DeclaresNameDescriptionSchemasAndAnnotations()
    {
        var tool = ToolOver(RecordGed).ToMcpServerTool();

        Assert.Equal(GetRecordTool.ToolName, tool.ProtocolTool.Name);
        Assert.Equal(GetRecordTool.Description, tool.ProtocolTool.Description);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations!.IdempotentHint);

        using var expectedInput = JsonDocument.Parse(GetRecordTool.InputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedInput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        using var expectedOutput = JsonDocument.Parse(GetRecordTool.OutputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedOutput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.OutputSchema!.Value));
    }

    [Fact]
    public void OutputSchemaJson_EveryRef_ResolvesToADefsEntry()
    {
        using var doc = JsonDocument.Parse(GetRecordTool.OutputSchemaJson);
        var defs = doc.RootElement.GetProperty("$defs");
        var defNames = defs.EnumerateObject().Select(p => p.Name).ToHashSet();

        void CheckRefs(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("$ref", out var refProp))
                {
                    string ptr = refProp.GetString()!;
                    Assert.StartsWith("#/$defs/", ptr);
                    Assert.Contains(ptr["#/$defs/".Length..], defNames);
                }
                foreach (var prop in element.EnumerateObject())
                    CheckRefs(prop.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CheckRefs(item);
            }
        }

        CheckRefs(doc.RootElement);
    }
}

namespace GedCore.Tests;

/// <summary>
/// Tests for the Media noun (Subproject I): createOrUpdateMedia / deleteMedia.
/// Covers content-addressed idempotency when xref is omitted, the
/// before-TRLR insertion fallback (the base fixture carries no OBJE),
/// portrait ordering, and the media-specific validation failures.
/// </summary>
public class ApplyMediaTests : ApplyTestBase
{
    private const string CreateWeddingPhoto = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateMedia",
            "title": "Wedding portrait, 1902",
            "files": [
              { "path": "media/allen-edna-wedding.jpg", "mediaType": "image/jpeg", "medium": "PHOTO", "title": "Allen and Edna" },
              { "path": "media/allen-edna-program.pdf", "mediaType": "application/pdf" }
            ],
            "attachTo": [ { "person": "@I00001@", "portrait": true } ] } ] } ] }
        """;

    [Fact]
    public void Create_NewMediaObjectLandsBeforeTrailer_AttachedAsPortrait()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(CreateWeddingPhoto);

        var doc = ReadDoc();
        var media = doc.ByXref["@M00001@"];
        Assert.Equal("OBJE", media.Tag);
        Assert.Equal("Wedding portrait, 1902", media.FirstChild("TITL")!.FullValue());

        var files = media.ChildrenByTag("FILE").ToList();
        Assert.Equal(2, files.Count);
        Assert.Equal("media/allen-edna-wedding.jpg", files[0].FullValue());
        Assert.Equal("image/jpeg", files[0].FirstChild("FORM")!.Value);
        Assert.Equal("PHOTO", files[0].FirstChild("FORM")!.FirstChild("MEDI")!.Value);
        Assert.Equal("Allen and Edna", files[0].FirstChild("TITL")!.FullValue());
        Assert.Equal("application/pdf", files[1].FirstChild("FORM")!.Value);
        Assert.Null(files[1].FirstChild("TITL"));

        // no OBJE existed before this run — the record must land just before TRLR
        Assert.Equal("TRLR", doc.Records[^1].Tag);
        Assert.Same(media, doc.Records[^2]);

        var allen = doc.ByXref["@I00001@"];
        var link = allen.FirstChild("OBJE")!;
        Assert.Equal("@M00001@", link.Value);
        Assert.Contains(result.Log, l => l.Contains("created (2 file(s))"));
        Assert.Contains(result.Log, l => l.Contains("attached (portrait)"));
    }

    [Fact]
    public void Reapplying_OmittedXref_IsContentAddressed_AndFullyIdempotent()
    {
        WriteBaseFile();
        RunExpectSuccess(CreateWeddingPhoto);

        var result = RunExpectSuccess(CreateWeddingPhoto);

        Assert.Equal(0, result.Deltas.GetValueOrDefault("OBJE"));
        Assert.Contains(result.Log, l => l.Contains("no-op (already matches)"));
        Assert.Contains(result.Log, l => l.Contains("no-op (already linked)"));
        Assert.Single(ReadDoc().Media());
    }

    [Fact]
    public void Update_SameXref_DifferentTitle_ReplacesAndLogs()
    {
        WriteBaseFile();
        RunExpectSuccess(CreateWeddingPhoto);

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@M00001@",
                "title": "Wedding portrait (Allen & Edna), 1902",
                "files": [
                  { "path": "media/allen-edna-wedding.jpg", "mediaType": "image/jpeg", "medium": "PHOTO", "title": "Allen and Edna" },
                  { "path": "media/allen-edna-program.pdf", "mediaType": "application/pdf" }
                ] } ] } ] }
            """);

        Assert.Equal("Wedding portrait (Allen & Edna), 1902",
            ReadDoc().ByXref["@M00001@"].FirstChild("TITL")!.FullValue());
        Assert.Contains(result.Log, l => l.Contains("TITL 'Wedding portrait, 1902' → 'Wedding portrait (Allen & Edna), 1902'"));
    }

    [Fact]
    public void Update_SameXref_DifferentFiles_ReplacesFileListWholesale()
    {
        WriteBaseFile();
        RunExpectSuccess(CreateWeddingPhoto);

        // Same record, retouched scan replaces both originals.
        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@M00001@",
                "title": "Wedding portrait, 1902",
                "files": [
                  { "path": "media/allen-edna-wedding-retouched.jpg", "mediaType": "image/jpeg", "medium": "PHOTO" }
                ] } ] } ] }
            """);

        var files = ReadDoc().ByXref["@M00001@"].ChildrenByTag("FILE").ToList();
        Assert.Single(files);
        Assert.Equal("media/allen-edna-wedding-retouched.jpg", files[0].FullValue());
        Assert.Equal("image/jpeg", files[0].FirstChild("FORM")!.Value);
        Assert.Contains(result.Log, l => l.Contains("FILE list replaced (2 → 1 file(s))"));

        // And the replacement itself is idempotent.
        var again = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@M00001@",
                "title": "Wedding portrait, 1902",
                "files": [
                  { "path": "media/allen-edna-wedding-retouched.jpg", "mediaType": "image/jpeg", "medium": "PHOTO" }
                ] } ] } ] }
            """);
        Assert.Contains(again.Log, l => l.Contains("no-op (already matches)"));
    }

    [Fact]
    public void Portrait_MovesLinkBeforeAnyExistingObjeLink()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@NewM1@",
                "files": [ { "path": "media/allen-childhood.jpg", "mediaType": "image/jpeg" } ],
                "attachTo": [ { "person": "@I00001@" } ] } ] } ] }
            """);

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@NewM2@",
                "files": [ { "path": "media/allen-wedding.jpg", "mediaType": "image/jpeg" } ],
                "attachTo": [ { "person": "@I00001@", "portrait": true } ] } ] } ] }
            """);

        var links = ReadDoc().ByXref["@I00001@"].ChildrenByTag("OBJE").Select(l => l.Value).ToList();
        Assert.Equal(["@M00002@", "@M00001@"], links);
    }

    [Fact]
    public void Create_BarePath_GainsTheRecommendedMediaPrefix()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "portrait.jpg", "mediaType": "image/jpeg" } ] } ] } ] }
            """);

        // GEDCOM 7 §2.12 recommends the "media/" prefix without requiring it;
        // a composer that omits it still gets a self-describing FILE payload.
        Assert.Equal("media/portrait.jpg",
            ReadDoc().ByXref["@M00001@"].ChildrenByTag("FILE").Single().FullValue());
    }

    [Fact]
    public void Create_PathAlreadyPrefixed_IsNotDoublePrefixed()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "media/portrait.jpg", "mediaType": "image/jpeg" } ] } ] } ] }
            """);

        Assert.Equal("media/portrait.jpg",
            ReadDoc().ByXref["@M00001@"].ChildrenByTag("FILE").Single().FullValue());
    }

    [Fact]
    public void Create_AbsoluteUrl_IsNeverPrefixed()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "https://example.org/portrait.jpg", "mediaType": "image/jpeg" } ] } ] } ] }
            """);

        Assert.Equal("https://example.org/portrait.jpg",
            ReadDoc().ByXref["@M00001@"].ChildrenByTag("FILE").Single().FullValue());
    }

    [Fact]
    public void Reapplying_BarePathAndPrefixedPath_AreTheSameFile_FullyIdempotent()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "portrait.jpg", "mediaType": "image/jpeg" } ] } ] } ] }
            """);

        // A second submission naming the same file, this time already
        // carrying the prefix, must resolve to the same OBJE record rather
        // than creating a duplicate -- normalization happens before the
        // content-addressed match, not after.
        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "media/portrait.jpg", "mediaType": "image/jpeg" } ] } ] } ] }
            """);

        Assert.Equal(0, result.Deltas.GetValueOrDefault("OBJE"));
        Assert.Contains(result.Log, l => l.Contains("no-op (already matches)"));
        Assert.Single(ReadDoc().Media());
    }

    [Fact]
    public void Validation_EmptyFiles_FailsCleanly()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "files": [] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("files required"));
    }

    [Fact]
    public void Validation_MalformedMediaType_FailsCleanly()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "media/x.jpg", "mediaType": "not-a-mime-type" } ] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("mediaType"));
    }

    [Fact]
    public void Validation_VoidAttachTarget_FailsCleanly()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "media/x.jpg", "mediaType": "image/jpeg" } ],
                "attachTo": [ { "person": "@VOID@" } ] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@VOID@ is not an addressable record"));
    }

    [Fact]
    public void Validation_DanglingAttachTarget_FailsCleanly()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "media/x.jpg", "mediaType": "image/jpeg" } ],
                "attachTo": [ { "person": "@I09999@" } ] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("attachTo target @I09999@ not in file"));
    }

    [Fact]
    public void Delete_RemovesRecordAndEveryLinkAcrossIndiAndFam()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia", "xref": "@NewM1@",
                "files": [ { "path": "media/family.jpg", "mediaType": "image/jpeg" } ],
                "attachTo": [ { "person": "@I00001@" }, { "family": "@F00002@" } ] } ] } ] }
            """);

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteMedia", "xref": "@M00001@" } ] } ] }
            """);

        var doc = ReadDoc();
        Assert.False(doc.ByXref.ContainsKey("@M00001@"));
        Assert.Empty(doc.ByXref["@I00001@"].ChildrenByTag("OBJE"));
        Assert.Empty(doc.ByXref["@F00002@"].ChildrenByTag("OBJE"));
        Assert.Contains(result.Log, l => l.Contains("deleted (2 link(s) removed)"));
    }

    [Fact]
    public void Delete_MissingXref_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteMedia", "xref": "@M00001@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@M00001@: not in file"));
    }
}

file static class GedDocumentExtensions
{
    public static IEnumerable<GedRecord> Media(this GedDocument doc) =>
        doc.Records.Where(r => r.Tag == "OBJE");
}

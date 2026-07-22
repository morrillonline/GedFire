using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Tests for multimedia in the generator model (Subproject H): OBJE records
/// become <see cref="GedMediaObject"/>s in <see cref="GedModel.Media"/>;
/// OBJE links on INDI/FAM/events resolve to <see cref="GedMediaLink"/>s in
/// document order, with the first link on an individual acting as their
/// portrait ("preferred first" rule).
/// </summary>
public class MediaModelTests
{
    static GedModel Build(string gedText) => ModelBuilder.Build(Ged55Parser.Parse(gedText));

    const string Ged = """
        0 @M1@ OBJE
        1 TITL Wedding portrait
        1 FILE media/photo1.jpg
        2 FORM image/jpeg
        2 TITL First scan
        1 FILE media/photo2.jpg
        2 FORM image/jpeg
        3 MEDI PHOTO
        0 @I1@ INDI
        1 NAME John /Hearth/
        1 SEX M
        1 OBJE @M1@
        2 TITL Portrait override
        1 OBJE @M1@
        """;

    [Fact]
    public void MediaObject_CollectsFilesAndFields()
    {
        var model = Build(Ged);

        var media = Assert.Single(model.Media.Values);
        Assert.Equal("Wedding portrait", media.Title);
        Assert.Equal(2, media.Files.Count);

        Assert.Equal("media/photo1.jpg", media.Files[0].Path);
        Assert.Equal("image/jpeg", media.Files[0].MediaType);
        Assert.Null(media.Files[0].Medium);
        Assert.Equal("First scan", media.Files[0].Title);

        Assert.Equal("media/photo2.jpg", media.Files[1].Path);
        Assert.Equal("PHOTO", media.Files[1].Medium);
    }

    [Fact]
    public void IndividualLinks_ResolveInOrder_FirstIsPortrait()
    {
        var model = Build(Ged);
        var indi = model.Individuals["@I1@"];

        Assert.Equal(2, indi.Media.Count);
        var portrait = indi.Media[0];
        Assert.Same(model.Media["@M1@"], portrait.Target);
        Assert.Equal("Portrait override", portrait.Title);
    }

    [Fact]
    public void DisplayTitle_PrefersLinkThenRecordThenFile()
    {
        var model = Build(Ged);
        var indi = model.Individuals["@I1@"];

        // Link[0] has its own TITL override.
        Assert.Equal("Portrait override", indi.Media[0].DisplayTitle);
        // Link[1] has no TITL override -> falls back to the record's TITL.
        Assert.Equal("Wedding portrait", indi.Media[1].DisplayTitle);

        // A media object with no record TITL falls back to its first file's TITL.
        string ged = """
            0 @M1@ OBJE
            1 FILE media/photo1.jpg
            2 FORM image/jpeg
            2 TITL Only the file has a title
            0 @I1@ INDI
            1 NAME John /Hearth/
            1 SEX M
            1 OBJE @M1@
            """;
        var m2 = Build(ged);
        Assert.Equal("Only the file has a title", m2.Individuals["@I1@"].Media[0].DisplayTitle);
    }

    [Fact]
    public void Crop_FullAndPartial_Parse_DanglingAndVoidLinksAreSkipped()
    {
        string ged = """
            0 @M1@ OBJE
            1 FILE media/photo1.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME John /Hearth/
            1 SEX M
            1 OBJE @M1@
            2 CROP
            3 TOP 10
            3 LEFT 20
            3 HEIGHT 100
            3 WIDTH 200
            1 OBJE @M1@
            2 CROP
            3 TOP 5
            1 OBJE @M99@
            1 OBJE @VOID@
            """;
        var model = Build(ged);
        var indi = model.Individuals["@I1@"];

        // Only the two links to the existing @M1@ record resolve.
        Assert.Equal(2, indi.Media.Count);

        var fullCrop = indi.Media[0].Crop;
        Assert.NotNull(fullCrop);
        Assert.Equal(10, fullCrop!.Top);
        Assert.Equal(20, fullCrop.Left);
        Assert.Equal(100, fullCrop.Height);
        Assert.Equal(200, fullCrop.Width);

        var partialCrop = indi.Media[1].Crop;
        Assert.NotNull(partialCrop);
        Assert.Equal(5, partialCrop!.Top);
        Assert.Null(partialCrop.Left);
        Assert.Null(partialCrop.Height);
        Assert.Null(partialCrop.Width);
    }

    [Fact]
    public void PrivatizedIndividual_HasEmptyMedia()
    {
        string ged = """
            0 @M1@ OBJE
            1 FILE media/photo1.jpg
            2 FORM image/jpeg
            0 @I1@ INDI
            1 NAME Barbara /Hearth/
            1 SEX F
            1 BIRT
            2 DATE 24 SEP 1990
            1 OBJE @M1@
            """;
        var model = Build(ged);
        PrivacyFilter.Apply(model, currentYear: 2026);

        Assert.Empty(model.Individuals["@I1@"].Media);
    }
}

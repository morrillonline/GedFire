using GedCore.Apply;

namespace GedCore.Tests;

/// <summary>
/// The escape/unescape helper shared by the Media changeset op and, later,
/// Subproject K's GEDZIP archive-path conversion.
/// </summary>
public class MediaPathsTests
{
    [Theory]
    [InlineData("media/photo.jpg", "media/photo.jpg")]
    [InlineData("media/wedding photo.jpg", "media/wedding%20photo.jpg")]
    [InlineData("media/félix.jpg", "media/f%C3%A9lix.jpg")]
    public void EscapeFilePath_PercentEscapesEachSegment_ButNotTheSlash(string logical, string escaped) =>
        Assert.Equal(escaped, MediaPaths.EscapeFilePath(logical));

    [Theory]
    [InlineData("media/photo.jpg")]
    [InlineData("media/wedding photo.jpg")]
    [InlineData("media/félix.jpg")]
    public void RoundTrips_ThroughEscapeThenUnescape(string logical) =>
        Assert.Equal(logical, MediaPaths.UnescapeFilePath(MediaPaths.EscapeFilePath(logical)));

    [Theory]
    [InlineData("http://example.org/photo.jpg")]
    [InlineData("https://example.org/photo with space.jpg")]
    public void AbsoluteUrls_PassThroughUnchangedInBothDirections(string url)
    {
        Assert.Equal(url, MediaPaths.EscapeFilePath(url));
        Assert.Equal(url, MediaPaths.UnescapeFilePath(url));
    }
}

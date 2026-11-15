namespace GedCore.Tests;

public class SourceNoteUrlTests
{
    [Theory]
    [InlineData("Parish Register, online at https://archive.example.org/reg/1.", "https://archive.example.org/reg/1")]
    [InlineData("Parish Register, online at https://archive.example.org/reg/1 (accessed 3 MAR 2024).", "https://archive.example.org/reg/1")]
    [InlineData("See (http://archive.example.org/a), too", "http://archive.example.org/a")]
    [InlineData("Online at HTTPS://Archive.Example.org/Reg", "HTTPS://Archive.Example.org/Reg")]
    public void Find_ReturnsTheAddressWithoutSentencePunctuation(string text, string expected) =>
        Assert.Equal(expected, SourceNoteUrl.Find(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A register with no address.")]
    [InlineData("Only the scheme https://.")]
    public void Find_ReturnsNullWhenThereIsNoAddress(string? text) =>
        Assert.Null(SourceNoteUrl.Find(text));

    [Fact]
    public void Find_ReturnsTheFirstOfSeveralAddresses() =>
        Assert.Equal("https://a.example.org/x", SourceNoteUrl.Find("https://a.example.org/x and https://b.example.org/y"));
}

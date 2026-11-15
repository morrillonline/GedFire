using GedCore.Matching;

namespace GedCore.Tests;

public class NameAgreementTests
{
    [Theory]
    [InlineData("Wm. /Morrill/", "William /Morrill/", true)]
    [InlineData("Mary Ann /Smith/", "Ann /Smith/", true)]
    [InlineData("Eula M /Hicks/", "Eula Marie /Hicks/", true)]
    [InlineData("Nathl /Morrill/", "Nathaniel /Morrill/", true)]
    [InlineData("Mary /Smith/", "Ann /Smith/", false)]
    [InlineData("Anne /____/", "Anne /____/", false)]
    [InlineData("Anne /Unknown/", "Anne /Unknown/", false)]
    [InlineData("Anne //", "Anne //", false)]
    [InlineData("Seth /Morrill/", "Nathaniel /Morrill/", false)]
    [InlineData("John /Morrill/", "John /Morrell/", false)]
    [InlineData("J /Morrill/", "John /Morrill/", false)]
    [InlineData("Ann Poole", "Ann Poole", true)]
    public void SharesAGivenName_NeedsTheSameKnownSurnameAndAWholeGivenName(string name, string other, bool expected)
    {
        Assert.Equal(expected, NameAgreement.SharesAGivenName(name, other));
        Assert.Equal(expected, NameAgreement.SharesAGivenName(other, name));
    }

    [Theory]
    [InlineData("Mason /Vale/", "Mason Sherlock /Vale/", true)]
    [InlineData("Wm Henry /Vale/", "William /Vale/", true)]
    [InlineData("Mason /Vale/", "John /Vale/", false)]
    [InlineData("Sherlock Mason /Vale/", "Mason /Vale/", false)]
    [InlineData("Mason /____/", "Mason /____/", false)]
    public void SharesFirstGivenName_ComparesOnlyTheFirstGivenName(string name, string other, bool expected) =>
        Assert.Equal(expected, NameAgreement.SharesFirstGivenName(name, other));

    [Fact]
    public void Display_DropsTheSurnameSlashes() =>
        Assert.Equal("Eula Marie Hicks", NameAgreement.Display("Eula Marie /Hicks/"));
}

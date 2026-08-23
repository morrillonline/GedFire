using System.Text;
using GedCore.Matching;

namespace GedCore.Tests;

public class NicknameDirectoryTests
{
    const string SyntheticJson = """
        {
          "male": {
            "robert": ["robert", "bob", "rob", "bobby"],
            "trey": ["rd", "trey"]
          },
          "female": {
            "margaret": ["margaret", "peggy", "meg"]
          }
        }
        """;

    static NicknameDirectory Synthetic() =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(SyntheticJson)));

    [Fact]
    public void FormalToNickname_BothDirections()
    {
        var dir = Synthetic();
        Assert.True(dir.AreEquivalent("Robert", "Bob", isMale: true));
        Assert.True(dir.AreEquivalent("Bob", "Robert", isMale: true));
    }

    [Fact]
    public void NicknameToNickname_SameGroup()
    {
        var dir = Synthetic();
        Assert.True(dir.AreEquivalent("Bob", "Bobby", isMale: true));
    }

    [Fact]
    public void UnrelatedNames_AreNotEquivalent()
    {
        var dir = Synthetic();
        Assert.False(dir.AreEquivalent("Robert", "Margaret", isMale: true));
        Assert.False(dir.AreEquivalent("Bob", "Bo", isMale: true));
    }

    [Fact]
    public void MaleName_NotFoundInFemaleMap()
    {
        var dir = Synthetic();
        // "Robert"/"Bob" are only documented in the male map; asking with an
        // explicit female sex must not find them there.
        Assert.False(dir.AreEquivalent("Robert", "Bob", isMale: false));
    }

    [Fact]
    public void FemaleName_NotFoundInMaleMap()
    {
        var dir = Synthetic();
        Assert.False(dir.AreEquivalent("Margaret", "Peggy", isMale: true));
    }

    [Fact]
    public void UnrecordedSex_UnionsBothMaps()
    {
        var dir = Synthetic();
        Assert.True(dir.AreEquivalent("Robert", "Bob", isMale: null));
        Assert.True(dir.AreEquivalent("Margaret", "Peggy", isMale: null));
    }

    [Fact]
    public void NamingConventionGroup_ParticipatesLikeAnyOther()
    {
        // A suffix-derived group label ("trey") is a naming convention, not
        // a name, but its list is loaded and matched the same as any group.
        var dir = Synthetic();
        Assert.True(dir.AreEquivalent("rd", "Trey", isMale: true));
    }

    [Fact]
    public void InputsAreNormalizedBeforeComparison()
    {
        var dir = Synthetic();
        Assert.True(dir.AreEquivalent("  robert ", "BOB!", isMale: true));
    }

    [Theory]
    [InlineData(null, "Bob")]
    [InlineData("", "Bob")]
    [InlineData("Robert", null)]
    [InlineData("Robert", "")]
    public void NullOrEmptyInput_IsNeverEquivalent(string? a, string? b)
    {
        var dir = Synthetic();
        Assert.False(dir.AreEquivalent(a, b, isMale: true));
    }

    [Fact]
    public void MissingMaps_LoadAsEmptyRatherThanThrowing()
    {
        var dir = new NicknameDirectory(new MemoryStream(Encoding.UTF8.GetBytes("{}")));
        Assert.False(dir.AreEquivalent("Robert", "Bob", isMale: true));
        Assert.False(dir.AreEquivalent("Margaret", "Peggy", isMale: null));
    }

    // -----------------------------------------------------------------------
    // Production dictionary (GedFire/Resources/nicknames.json, embedded)
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadEmbedded_FindsDocumentedReferencePairs()
    {
        var dir = NicknameDirectory.LoadEmbedded();

        Assert.True(dir.AreEquivalent("Fred", "Frederick", isMale: true));
        Assert.True(dir.AreEquivalent("Bill", "William", isMale: true));

        // An exact-name pair with no documented relationship at all.
        Assert.False(dir.AreEquivalent("Fred", "William", isMale: true));
    }
}

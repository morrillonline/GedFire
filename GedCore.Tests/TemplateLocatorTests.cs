using GedFire;

namespace GedCore.Tests;

public class TemplateLocatorTests : IDisposable
{
    readonly string _root;

    public TemplateLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gedfire-tpl-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(_root, "data"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    string InputGed => Path.Combine(_root, "data", "test.ged");

    [Fact]
    public void ExplicitPath_AlwaysWins()
    {
        string explicitPath = Path.Combine(_root, "custom.html");
        Assert.Equal(explicitPath, TemplateLocator.Locate(InputGed, explicitPath));
    }

    [Fact]
    public void ModernTemplate_IsPreferredProbe()
    {
        string modern = Path.Combine(_root, "content", "template", "GedfireModernTemplate.html");
        string legacy = Path.Combine(_root, "gedfire", "GedfireTemplate.html");
        Directory.CreateDirectory(Path.GetDirectoryName(modern)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(modern, "modern");
        File.WriteAllText(legacy, "legacy");

        Assert.Equal(modern, TemplateLocator.Locate(InputGed, null));
    }

    [Fact]
    public void LegacyTemplate_FoundWhenModernAbsent()
    {
        string legacy = Path.Combine(_root, "gedfire", "GedfireTemplate.html");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "legacy");

        Assert.Equal(legacy, TemplateLocator.Locate(InputGed, null));
    }

    [Fact]
    public void NothingFound_ReturnsNull()
    {
        Assert.Null(TemplateLocator.Locate(InputGed, null));
    }
}

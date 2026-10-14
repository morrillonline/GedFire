using System.Text;
using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class ArgumentFileTests
{
    static MemoryStream Utf8(string json) => new(Encoding.UTF8.GetBytes(json));

    static JsonElement Arg(string? json) => json is null ? default : JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void TheNamedProperty_IsReturned()
    {
        bool ok = ArgumentFile.TryRead(Utf8("""{ "findings": [ { "id": "F-1" } ] }"""), "findings", "inputPath x", out var value, out var error);

        Assert.True(ok, error);
        Assert.Equal(JsonValueKind.Array, value.ValueKind);
        Assert.Equal("F-1", value[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("""[ 1 ]""", "not an array")]
    [InlineData("""{ }""", "has no \"findings\" property")]
    [InlineData("""{ "findings": [], "extra": 1 }""", "extra is not recognized")]
    [InlineData("""{ "findings": [""", "is not valid JSON")]
    public void AFileOfTheWrongShape_IsRefusedWithAReason(string json, string expected)
    {
        bool ok = ArgumentFile.TryRead(Utf8(json), "findings", "inputPath x", out _, out var error);

        Assert.False(ok);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void AnInlineArgumentWithNoPath_IsPassedThrough()
    {
        bool ok = ArgumentFile.TryResolve(Arg("[1]"), "findings", default, "inputPath", out var value, out var error);

        Assert.True(ok, error);
        Assert.Equal(JsonValueKind.Array, value.ValueKind);
    }

    [Fact]
    public void NeitherInlineNorPath_LeavesTheArgumentAbsent()
    {
        bool ok = ArgumentFile.TryResolve(default, "findings", default, "inputPath", out var value, out _);

        Assert.True(ok);
        Assert.Equal(JsonValueKind.Undefined, value.ValueKind);
    }

    [Fact]
    public void BothInlineAndPath_IsRefused()
    {
        bool ok = ArgumentFile.TryResolve(Arg("[1]"), "findings", Arg("\"in.json\""), "inputPath", out _, out var error);

        Assert.False(ok);
        Assert.Equal("give findings or inputPath, not both.", error);
    }

    [Fact]
    public void ABlankPath_IsRefused()
    {
        bool ok = ArgumentFile.TryResolve(default, "findings", Arg("\"  \""), "inputPath", out _, out var error);

        Assert.False(ok);
        Assert.Equal("inputPath must not be blank.", error);
    }

    [Fact]
    public void APathThatIsNotAString_IsRefused()
    {
        bool ok = ArgumentFile.TryResolve(default, "findings", Arg("5"), "inputPath", out _, out var error);

        Assert.False(ok);
        Assert.Contains("inputPath must be a string", error);
    }

    [Fact]
    public void AMissingFile_IsRefusedByName()
    {
        string missing = Path.Combine(Path.GetTempPath(), "gedfire-no-such-dir", "in.json");

        bool ok = ArgumentFile.TryResolve(default, "findings", Arg(JsonSerializer.Serialize(missing)), "inputPath", out _, out var error);

        Assert.False(ok);
        Assert.Contains("inputPath", error);
        Assert.Contains("could not be read", error);
    }
}

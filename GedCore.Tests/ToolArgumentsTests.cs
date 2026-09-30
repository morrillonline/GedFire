using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class ToolArgumentsTests
{
    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void XrefList_Valid_ReturnsTrimmedXrefsInOrder()
    {
        bool ok = ToolArguments.TryReadXrefList(Json("[\" @I1@ \",\"@F2@\",\"@I1@\"]"), "xrefs", 10, out var xrefs, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(["@I1@", "@F2@", "@I1@"], xrefs);
    }

    [Theory]
    [InlineData("\"@I1@\"", "xrefs must be an array of xref strings", "a string")]
    [InlineData("[1]", "xrefs[0] must be an xref string", "a number")]
    [InlineData("[\"I1\"]", "xrefs[0] must be an xref such as", "\"I1\"")]
    [InlineData("[\"@I1@\", null]", "xrefs[1] must be an xref string", "null")]
    [InlineData("[]", "xrefs must contain 1 to 3 xrefs, got 0", "0")]
    [InlineData("[\"@I1@\",\"@I2@\",\"@I3@\",\"@I4@\"]", "xrefs must contain 1 to 3 xrefs, got 4", "4")]
    public void XrefList_WrongShape_NamesTheFieldAndTheAcceptedShape(string json, string expectedStart, string expectedDetail)
    {
        bool ok = ToolArguments.TryReadXrefList(Json(json), "xrefs", 3, out _, out var error);

        Assert.False(ok);
        Assert.Contains(expectedStart, error);
        Assert.Contains(expectedDetail, error);
    }

    [Fact]
    public void XrefList_Absent_SaysTheFieldIsRequired()
    {
        bool ok = ToolArguments.TryReadXrefList(default, "xrefs", 3, out _, out var error);

        Assert.False(ok);
        Assert.Contains("xrefs is required", error);
    }

    [Fact]
    public void OptionalStringList_AbsentOrNull_IsEmpty()
    {
        Assert.True(ToolArguments.TryReadOptionalStringList(default, "surnames", out var none, out _));
        Assert.Empty(none);
        Assert.True(ToolArguments.TryReadOptionalStringList(Json("null"), "surnames", out var nulled, out _));
        Assert.Empty(nulled);
    }

    [Theory]
    [InlineData("\"Smith\"", "surnames must be an array")]
    [InlineData("[\"Smith\", 3]", "surnames[1] must be a non-blank string, not a number")]
    [InlineData("[\"  \"]", "surnames[0] must be a non-blank string, not a blank string")]
    public void OptionalStringList_WrongShape_NamesTheField(string json, string expected)
    {
        Assert.False(ToolArguments.TryReadOptionalStringList(Json(json), "surnames", out _, out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void OptionalStringList_Valid_TrimsEntries()
    {
        Assert.True(ToolArguments.TryReadOptionalStringList(Json("[\" Ashworth \",\"Ashwirth\"]"), "surnames", out var list, out _));
        Assert.Equal(["Ashworth", "Ashwirth"], list);
    }

    [Fact]
    public void OptionalString_NonString_NamesTheField()
    {
        Assert.False(ToolArguments.TryReadOptionalString(Json("5"), "cursor", out _, out var error));
        Assert.Contains("cursor must be a string, not a number", error);
    }

    [Fact]
    public void OptionalInt_AbsentUsesDefault_ValidPasses()
    {
        Assert.True(ToolArguments.TryReadOptionalInt(default, "pageSize", 1, 10, 7, out int defaulted, out _));
        Assert.Equal(7, defaulted);
        Assert.True(ToolArguments.TryReadOptionalInt(Json("4"), "pageSize", 1, 10, 7, out int given, out _));
        Assert.Equal(4, given);
    }

    [Theory]
    [InlineData("\"4\"")]
    [InlineData("4.5")]
    [InlineData("0")]
    [InlineData("11")]
    public void OptionalInt_StringFractionOrOutOfRange_IsRejectedByName(string json)
    {
        Assert.False(ToolArguments.TryReadOptionalInt(Json(json), "pageSize", 1, 10, 7, out _, out var error));
        Assert.Contains("pageSize must be an integer between 1 and 10", error);
    }
}

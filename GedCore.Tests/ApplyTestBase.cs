using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>
/// Shared fixture for the changeset-applier suites: a synthetic GEDCOM 7 file
/// in a temp path plus run/read helpers. The fixture deliberately carries the
/// ambiguity cases the dialect must handle: a person with two same-tag facts
/// (Nellie's two CENS) and a couple sharing two families (Allen and Edna).
/// </summary>
public abstract class ApplyTestBase
{
    private byte[] _currentBytes = [];

    // @I00001@ Allen — child of @F00001@, partner in @F00002@ and @F00003@
    // @I00002@ Harvey / @I00003@ Nellie — Allen's parents (@F00001@, no MARR)
    // @I00003@ Nellie — two CENS facts (fact-selector ambiguity)
    // @I00004@ Edna — Allen's wife in BOTH @F00002@ and @F00003@ (shared-family ambiguity)
    protected static readonly string[] BaseLines =
    [
        "0 HEAD",
        "1 GEDC",
        "2 VERS 7.0",
        "0 @I00001@ INDI",
        "1 NAME Allen /Test/",
        "1 SEX M",
        "1 BIRT",
        "2 DATE 1928",
        "2 PLAC Minnesota",
        "1 FAMS @F00002@",
        "1 FAMS @F00003@",
        "1 FAMC @F00001@",
        "0 @I00002@ INDI",
        "1 NAME Harvey /Test/",
        "1 SEX M",
        "1 FAMS @F00001@",
        "0 @I00003@ INDI",
        "1 NAME Nellie /Test/",
        "1 SEX F",
        "1 CENS",
        "2 DATE 1930",
        "2 PLAC Minnesota",
        "1 CENS",
        "2 DATE 1940",
        "2 PLAC Minnesota",
        "1 FAMS @F00001@",
        "0 @I00004@ INDI",
        "1 NAME Edna /Test/",
        "1 SEX F",
        "1 FAMS @F00002@",
        "1 FAMS @F00003@",
        "0 @F00001@ FAM",
        "1 HUSB @I00002@",
        "1 WIFE @I00003@",
        "1 CHIL @I00001@",
        "0 @F00002@ FAM",
        "1 HUSB @I00001@",
        "1 WIFE @I00004@",
        "0 @F00003@ FAM",
        "1 HUSB @I00001@",
        "1 WIFE @I00004@",
        "0 @S00001@ SOUR",
        "1 TITL Existing source",
        "0 TRLR",
    ];

    protected byte[] WriteBaseFile() => WriteFile(BaseLines);

    /// <summary>Same as <see cref="WriteBaseFile"/>, but from caller-supplied lines instead of <see cref="BaseLines"/>.</summary>
    protected byte[] WriteFile(IReadOnlyList<string> lines)
    {
        var document = Ged70Parser.Parse(string.Join("\r\n", lines) + "\r\n");
        var output = new MemoryStream();
        Ged70Formatter.Write(document, output);
        return _currentBytes = output.ToArray();
    }

    protected ApplyResult Run(string changesetJson, int[]? items = null, bool dryRun = false, DateTime? utcNow = null)
    {
        return Run(Changeset.Parse(changesetJson), items, dryRun, utcNow);
    }

    protected ApplyResult Run(Changeset changeset, int[]? items = null, bool dryRun = false, DateTime? utcNow = null)
    {
        var result = ChangesetApplier.Run(
            _currentBytes, changeset, items ?? [.. changeset.Items.Select(i => i.Number)], dryRun, utcNow);
        if (result.Success && !dryRun && result.OutputBytes is not null)
            _currentBytes = result.OutputBytes;
        return result;
    }

    protected ApplyResult RunExpectSuccess(string changesetJson, int[]? items = null, DateTime? utcNow = null)
    {
        var result = Run(changesetJson, items, utcNow: utcNow);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result;
    }

    protected ApplyResult RunExpectSuccess(Changeset changeset, int[]? items = null, DateTime? utcNow = null)
    {
        var result = Run(changeset, items, utcNow: utcNow);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result;
    }

    protected byte[] ReadBytes() => _currentBytes;

    protected GedDocument ReadDoc() => Ged70Parser.Read(new MemoryStream(_currentBytes));
}

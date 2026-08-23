using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>
/// Verifies that applying a changeset to a GEDCOM 7 file never disturbs
/// unrelated extension tags. GedFire's dialect never emits extension tags
/// itself, but a master file may legitimately carry them (e.g. carried over
/// from another tool), so the applier must treat them as opaque, untouched
/// structures rather than stripping them as a side effect of editing a
/// sibling fact.
///
/// The fixture is seeded once rather than rebuilt per test and follows the
/// syntax of the official FamilySearch GEDCOM 7 extension-tag example.
/// </summary>
public class ExtensionTagPreservationTests
{
    private const string FixtureJson = """
        { "items": [
          { "item": 1, "target": "@I00001@", "ops": [
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
              "value": "Albin H. /Test/", "match": "Allen /Test/",
              "citation": { "source": "@S00001@", "page": "p. 1",
                            "dataText": "name corrected", "quay": 2 } },
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
              "value": { "place": "Fergus Falls, Minnesota" },
              "citation": { "source": "@S00001@", "page": "p. 1",
                            "dataText": "birthplace refined", "quay": 2 } },
            { "op": "createOrUpdateNote", "record": "@I00001@",
              "text": "Note added by test." } ] },
          { "item": 2, "target": "@F00001@", "ops": [
            { "op": "createOrUpdateVital", "record": "@F00001@", "fact": "MARR",
              "value": { "place": "Minnesota" },
              "citation": { "source": "@S00001@", "page": "p. 2",
                            "dataText": "married in Minnesota", "quay": 2 } } ] }
        ] }
        """;

    private static byte[] ReadFixture()
    {
        var stream = typeof(ExtensionTagPreservationTests).Assembly
            .GetManifestResourceStream("GedCore.Tests.TestData.Extensions-7.0.ged")
            ?? throw new InvalidOperationException(
                "Embedded resource GedCore.Tests.TestData.Extensions-7.0.ged not found.");
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }

    [Fact]
    public void Apply_PreservesExtensionTags_WhileChangingUnrelatedFacts()
    {
        byte[] original = ReadFixture();
        var changeset = Changeset.Parse(FixtureJson);

        var result = ChangesetApplier.Run(original, changeset,
            [.. changeset.Items.Select(i => i.Number)], dryRun: false);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.NotNull(result.OutputBytes);

        var doc = Ged70Parser.Read(new MemoryStream(result.OutputBytes!));

        // The requested changes actually happened — otherwise the
        // preservation assertions below would be vacuous.
        var indi = doc.ByXref["@I00001@"];
        Assert.Equal("Albin H. /Test/", indi.FirstChild("NAME")!.Value);
        Assert.Equal("Fergus Falls, Minnesota", indi.FirstChild("BIRT")!.FirstChild("PLAC")!.Value);
        Assert.Contains("Note added by test.", indi.ChildrenByTag("NOTE").Select(n => n.Value));

        var fam = doc.ByXref["@F00001@"];
        Assert.Equal("Minnesota", fam.FirstChild("MARR")!.FirstChild("PLAC")!.Value);

        // Header SCHMA extension-tag declarations are untouched.
        var schma = doc.Records.First(r => r.Tag == "HEAD").FirstChild("SCHMA")!;
        var tags = schma.ChildrenByTag("TAG").Select(t => t.Value).ToList();
        Assert.Equal(
            [
                "_MILT https://example.com/gedcom/MILT",
                "_NICKSRC https://example.com/gedcom/NICKSRC",
                "_ANNIV https://example.com/gedcom/ANNIV",
            ],
            tags);

        // _NICKSRC under NAME survived the NAME value replacement.
        var nickSrc = indi.FirstChild("NAME")!.FirstChild("_NICKSRC");
        Assert.NotNull(nickSrc);
        Assert.Equal("Provided by military discharge papers", nickSrc!.Value);

        // _MILT (a sibling of the edited BIRT/NOTE) survived intact.
        var milt = indi.FirstChild("_MILT");
        Assert.NotNull(milt);
        Assert.Equal("1950", milt!.FirstChild("DATE")!.Value);
        Assert.Equal("Fort Snelling", milt.FirstChild("PLAC")!.Value);
        Assert.Equal("Served during the Korean War", milt.FirstChild("NOTE")!.Value);

        // _ANNIV (a sibling of the edited MARR) survived intact.
        var anniv = fam.FirstChild("_ANNIV");
        Assert.NotNull(anniv);
        Assert.Equal("2000", anniv!.FirstChild("DATE")!.Value);
        Assert.Equal("Fiftieth wedding anniversary celebration", anniv.FirstChild("NOTE")!.Value);
    }
}

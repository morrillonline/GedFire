using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Shared helper for the GedFire.Match test suites: build a GedModel from a
/// small synthetic GEDCOM 5.5 fixture and look up an individual by xref for
/// assertions. Not itself a production class under test.
/// </summary>
internal static class MatchTestModels
{
    public static GedModel Build(string gedText) =>
        ModelBuilder.Build(Ged55Parser.Parse(gedText));

    public static GedIndividual Person(GedModel model, string xref) =>
        model.Individuals[xref];
}

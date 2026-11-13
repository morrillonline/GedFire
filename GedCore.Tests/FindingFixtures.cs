namespace GedCore.Tests;

/// <summary>Loads the embedded, synthetic finding files under TestData/Findings.</summary>
public static class FindingFixtures
{
    const string Prefix = "GedCore.Tests.TestData.Findings.";

    public static IReadOnlyList<string> Names { get; } =
        [.. typeof(FindingFixtures).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..])
            .Order(StringComparer.Ordinal)];

    public static Stream Open(string name) =>
        typeof(FindingFixtures).Assembly.GetManifestResourceStream(Prefix + name)
        ?? throw new InvalidOperationException($"Finding fixture '{name}' was not found.");

    public static string Read(string name)
    {
        using var reader = new StreamReader(Open(name));
        return reader.ReadToEnd();
    }
}

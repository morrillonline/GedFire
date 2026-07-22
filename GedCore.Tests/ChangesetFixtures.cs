using GedCore.Apply;

namespace GedCore.Tests;

/// <summary>Loads embedded, reviewable examples of the changeset dialect.</summary>
public static class ChangesetFixtures
{
    public static Changeset Load(string name) => Changeset.Parse(Read(name));

    public static string Read(string name)
    {
        var stream = typeof(ChangesetFixtures).Assembly.GetManifestResourceStream(
            $"GedCore.Tests.TestData.Changesets.{name}.json")
            ?? throw new InvalidOperationException($"Changeset fixture '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
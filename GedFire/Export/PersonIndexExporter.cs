using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GedCore;
using GedFire.Gen;

namespace GedFire.Export;

// ---------------------------------------------------------------------------
// Research person index (researchplan.md §6.1) — serializes the ModelBuilder
// model to the JSON consumed by the research pipeline's correlation stage:
// every individual with xref, normalized name, birth/death (raw date + year +
// qualifier + place), parents' xrefs, and per-marriage spouse/children xrefs.
//
// Output is one compact JSON object per person so that regenerating the index
// after an assimilation produces a per-person git diff.
// ---------------------------------------------------------------------------

public static class PersonIndexExporter
{
    static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteFile(GedModel model, string sourcePath, string outputPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, ToJson(model, sourcePath), new UTF8Encoding(false));
    }

    public static string ToJson(GedModel model, string source)
    {
        var persons = model.SortedIndividuals.Select(ToEntry).ToList();

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"generated\": ")
          .Append(JsonSerializer.Serialize(DateTime.UtcNow.ToString("yyyy-MM-dd")))
          .Append(",\n");
        sb.Append("  \"source\": ")
          .Append(JsonSerializer.Serialize(source.Replace('\\', '/')))
          .Append(",\n");
        sb.Append("  \"count\": ").Append(persons.Count).Append(",\n");
        sb.Append("  \"persons\": [\n");
        for (int i = 0; i < persons.Count; i++)
        {
            sb.Append("    ").Append(JsonSerializer.Serialize(persons[i], CompactOptions));
            sb.Append(i < persons.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    // -----------------------------------------------------------------------

    static PersonEntry ToEntry(GedIndividual indi) => new()
    {
        Xref    = indi.Xref,
        Name    = (indi.FirstMiddle() + " " + indi.LastName).Trim(),
        Surname = Normalize(indi.LastName),
        Given   = Normalize(indi.FirstMiddle()),
        Birth   = ToEventEntry(indi.Birth),
        Death   = ToEventEntry(indi.Death),
        Parents = ToParents(indi.FamChild),
        Families = indi.FamSpouse.Select(f => ToFamily(indi, f)).ToList(),
    };

    static EventEntry? ToEventEntry(GedEvent? ev)
    {
        if (ev is null) return null;
        string? date  = ev.Date.Length  > 0 ? ev.Date  : null;
        string? place = ev.Place.Length > 0 ? ev.Place : null;
        if (date is null && place is null) return null;

        int year = GedDate.ParseYear(ev.Date);
        return new EventEntry
        {
            Date      = date,
            Year      = year > 0 ? year : null,
            Qualifier = GedDate.Qualifier(ev.Date),
            Place     = place,
        };
    }

    static ParentsEntry? ToParents(GedFamily? famChild) =>
        famChild is null ? null : new ParentsEntry
        {
            Family = famChild.Xref,
            Father = famChild.Husband?.Xref,
            Mother = famChild.Wife?.Xref,
        };

    static FamilyEntry ToFamily(GedIndividual indi, GedFamily fam) => new()
    {
        Xref     = fam.Xref,
        Spouse   = fam.SpouseOf(indi)?.Xref,
        Marriage = ToEventEntry(fam.Marriage),
        Children = fam.Children.Select(c => c.Xref).ToList(),
    };

    // Matching normalization, shared with the MCP find_person lookup
    // (GedCore.Matching.PersonNameNormalizer): uppercase, punctuation stripped,
    // hyphens preserved so a hyphenated surname stays one token. Underscores
    // survive — "____" is the unknown-name placeholder throughout this dataset.
    static string Normalize(string name) => GedCore.Matching.PersonNameNormalizer.Normalize(name);

    // -----------------------------------------------------------------------
    // Serialization shapes (public properties so reflection-based
    // System.Text.Json can read them; the types themselves stay private).
    // -----------------------------------------------------------------------

    sealed record PersonEntry
    {
        public required string Xref { get; init; }
        public required string Name { get; init; }
        public required string Surname { get; init; }
        public required string Given { get; init; }
        public EventEntry? Birth { get; init; }
        public EventEntry? Death { get; init; }
        public ParentsEntry? Parents { get; init; }
        public required List<FamilyEntry> Families { get; init; }
    }

    sealed record EventEntry
    {
        public string? Date { get; init; }
        public int? Year { get; init; }
        public string? Qualifier { get; init; }
        public string? Place { get; init; }
    }

    sealed record ParentsEntry
    {
        public required string Family { get; init; }
        public string? Father { get; init; }
        public string? Mother { get; init; }
    }

    sealed record FamilyEntry
    {
        public required string Xref { get; init; }
        public string? Spouse { get; init; }
        public EventEntry? Marriage { get; init; }
        public required List<string> Children { get; init; }
    }
}

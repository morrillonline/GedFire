using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GedFire.TargetSelection;

// ---------------------------------------------------------------------------
// Serializes a target-selection draw to wanted.json: enough per target that
// a research assistant can start work without further context.
// ---------------------------------------------------------------------------

public static class WantedFileWriter
{
    static readonly JsonSerializerOptions Options = BuildOptions();

    static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static void WriteFile(
        string sourcePath, IReadOnlyCollection<string> surnames, int totalCandidates,
        DrawResult draw, string outputPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, ToJson(sourcePath, surnames, totalCandidates, draw), new UTF8Encoding(false));
    }

    public static string ToJson(
        string sourcePath, IReadOnlyCollection<string> surnames, int totalCandidates, DrawResult draw) =>
        Serialize(sourcePath.Replace('\\', '/'), surnames, totalCandidates, draw);

    /// <summary>The same document without the GEDCOM path, for callers that must not learn where the file lives.</summary>
    public static string ToJson(IReadOnlyCollection<string> surnames, int totalCandidates, DrawResult draw) =>
        Serialize(null, surnames, totalCandidates, draw);

    /// <summary>The document for an explicit-xrefs scoring: no surnames, a null seed, and the input xrefs that produced no target.</summary>
    public static string ToJsonForXrefs(IReadOnlyList<string> xrefsWithoutGaps, DrawResult draw) =>
        Serialize(null, [], draw.Targets.Count, draw, xrefsWithoutGaps);

    static string Serialize(
        string? source, IReadOnlyCollection<string> surnames, int totalCandidates, DrawResult draw,
        IReadOnlyList<string>? xrefsWithoutGaps = null)
    {
        var envelope = new WantedFile
        {
            Generated = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Source = source,
            Surnames = [.. surnames],
            TotalCandidates = totalCandidates,
            Count = draw.Targets.Count,
            Targets = [.. draw.Targets],
            SuppressedCards = draw.SuppressedCards,
            XrefsWithoutGaps = xrefsWithoutGaps,
            Draw = new DrawLogEntry { Seed = draw.Seed, LegendaryDiscards = [.. draw.LegendaryDiscards] },
        };
        return JsonSerializer.Serialize(envelope, Options);
    }

    sealed record WantedFile
    {
        public required string Generated { get; init; }
        public string? Source { get; init; }
        public required List<string> Surnames { get; init; }
        public required int TotalCandidates { get; init; }
        public required int Count { get; init; }
        public required List<SelectionTarget> Targets { get; init; }
        public required IReadOnlyList<SuppressedCard> SuppressedCards { get; init; }
        public IReadOnlyList<string>? XrefsWithoutGaps { get; init; }
        public required DrawLogEntry Draw { get; init; }
    }

    sealed record DrawLogEntry
    {
        // A null seed is part of the contract (explicit xrefs, nothing drawn), so it must not be omitted.
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public required long? Seed { get; init; }
        public required List<DiscardedTarget> LegendaryDiscards { get; init; }
    }
}

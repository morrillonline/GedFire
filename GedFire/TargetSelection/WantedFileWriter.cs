using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GedFire.TargetSelection;

// ---------------------------------------------------------------------------
// Serializes a target-selection draw to wanted.json: enough per target that
// a research assistant can start work without further context. Replaces the
// external study script's gap/goal/queries shape with cardType/difficulty/
// score (docs/design/target-selection.md).
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
        string sourcePath, IReadOnlyCollection<string> surnames, int totalCandidates, DrawResult draw)
    {
        var envelope = new WantedFile
        {
            Generated = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Source = sourcePath.Replace('\\', '/'),
            Surnames = [.. surnames],
            TotalCandidates = totalCandidates,
            Count = draw.Targets.Count,
            Targets = [.. draw.Targets],
            Draw = new DrawLogEntry { Seed = draw.Seed, LegendaryDiscards = [.. draw.LegendaryDiscards] },
        };
        return JsonSerializer.Serialize(envelope, Options);
    }

    sealed record WantedFile
    {
        public required string Generated { get; init; }
        public required string Source { get; init; }
        public required List<string> Surnames { get; init; }
        public required int TotalCandidates { get; init; }
        public required int Count { get; init; }
        public required List<SelectionTarget> Targets { get; init; }
        public required DrawLogEntry Draw { get; init; }
    }

    sealed record DrawLogEntry
    {
        public required long Seed { get; init; }
        public required List<DiscardedTarget> LegendaryDiscards { get; init; }
    }
}

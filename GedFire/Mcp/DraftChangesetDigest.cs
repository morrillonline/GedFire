using System.Text.Json.Nodes;
using GedCore.Drafting;

namespace GedFire.Mcp;

/// <summary>One item of the drafted changeset as the digest reports it: its number, its target, and how many ops it carries.</summary>
public sealed record DraftItemDigest(int Item, string Target, int Ops);

/// <summary>
/// The quiet response draft_changeset returns when reportPath is given without verbose: a
/// digest of the result the report file holds in full, so drafting an item does not fill
/// the caller's context with a changeset it will read from the file.
/// </summary>
public sealed record DraftChangesetDigest(
    bool Success, IReadOnlyList<DraftItemDigest> Items, string DecisionsSummary, IReadOnlyList<string> Errors,
    string? WrittenTo, string? ReportWrittenTo)
{
    /// <summary>The digest of <paramref name="result"/> after it was written to its report file.</summary>
    public static DraftChangesetDigest Of(DraftChangesetResult result) =>
        new(result.Success, ItemDigests(result.Changeset), Summarize(result.Decisions), result.Errors,
            result.WrittenTo, result.ReportWrittenTo);

    static IReadOnlyList<DraftItemDigest> ItemDigests(JsonObject? changeset) =>
        changeset?["items"] is not JsonArray items
            ? []
            : [.. items.OfType<JsonObject>().Select(item => new DraftItemDigest(
                (int?)item["item"] ?? 0,
                (string?)item["target"] ?? "",
                item["ops"] is JsonArray ops ? ops.Count : 0))];

    static string Summarize(IReadOnlyList<DraftDecision> decisions) =>
        decisions.Count == 0
            ? "no decisions"
            : $"{decisions.Count} decisions: " +
              string.Join(", ", decisions.GroupBy(d => d.Kind).Select(g => $"{g.Count()} {g.Key}"));
}

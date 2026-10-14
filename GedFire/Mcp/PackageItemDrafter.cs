using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;
using GedCore.Points;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Drafts, checks and writes one package item: compiles the anchor's findings,
// dry-runs the result against the bound document once (which yields both the
// points preview and the plausibility findings), and writes the changeset file
// only when all of it passes, reporting the hash of the bytes written.
// ---------------------------------------------------------------------------

/// <summary>What happened to one package item.</summary>
public sealed record PackageItemResult(
    int Item,
    string Anchor,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> FindingIds,
    bool Success,
    string? WrittenTo,
    string? Sha256,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DraftDecision> Decisions,
    PointsPreview? PointsPreview,
    IReadOnlyList<PlausibilityFinding> Plausibility);

sealed class PackageItemDrafter(
    ChangesetDrafter drafter, string documentPath, IReadOnlyDictionary<string, IReadOnlyList<PointsCard>> cards)
{
    public PackageItemResult Draft(PackageItemPlan plan, string outputPath, bool overwrite, CancellationToken cancellationToken)
    {
        var files = plan.Files.Select(f => f.Name).ToList();
        var ids = plan.Files.Where(f => f.Finding is not null).Select(f => f.Finding!.Id).ToList();
        PackageItemResult Fail(IEnumerable<string> errors, IReadOnlyList<DraftDecision>? decisions = null,
            PointsPreview? preview = null, IReadOnlyList<PlausibilityFinding>? plausibility = null) =>
            new(plan.Number, plan.Anchor, files, ids, false, null, null, [.. errors], decisions ?? [], preview, plausibility ?? []);

        var unready = plan.Files.SelectMany(f => f.Errors).ToList();
        if (unready.Count > 0) return Fail(unready);

        var findings = DraftNoteJoiner.Join([.. plan.Files.Select(f => f.Finding!)]);
        var draft = drafter.Draft(findings);
        if (!draft.Success) return Fail(FindingFileLoader.NameFiles([.. draft.Errors], files), draft.Decisions);

        var check = DryRun(draft.Changeset!.ToJsonString(), cancellationToken);
        if (check.Error is not null) return Fail([check.Error], draft.Decisions);

        var preview = cards.TryGetValue(plan.Anchor, out var anchorCards) ? CardScorer.Preview(anchorCards, check.Result!.Changes) : null;
        var plausibility = check.Result!.NewDiagnostics
            .Where(d => d.Code.StartsWith("GEN", StringComparison.Ordinal))
            .Select(d => new PlausibilityFinding(d.Code, d.Severity.ToString(), d.Message, d.Xref))
            .ToList();
        if (!check.Result.Success) return Fail(check.Result.Errors, draft.Decisions, preview, plausibility);

        byte[] bytes = ChangesetFileWriter.Serialize(draft.Changeset);
        if (ChangesetFileWriter.TryWrite(outputPath, bytes, overwrite, out string? writeError) is null)
            return Fail([writeError!], draft.Decisions, preview, plausibility);

        return new PackageItemResult(
            plan.Number, plan.Anchor, files, ids, true, outputPath, ChangesetFileWriter.Sha256Hex(bytes),
            [], draft.Decisions, preview, plausibility);
    }

    (ApplyResult? Result, string? Error) DryRun(string json, CancellationToken cancellationToken)
    {
        try
        {
            var changeset = Changeset.Parse(json);
            var result = ChangesetApplier.Run(
                documentPath, changeset, [.. changeset.Items.Select(i => i.Number)], dryRun: true, cancellationToken: cancellationToken);
            return (result, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not open '{documentPath}': {ex.Message}");
        }
        catch (JsonException ex)
        {
            return (null, $"The drafted changeset could not be read back: {ex.Message}");
        }
    }
}

using System.Text.Json;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The optional reportPath of the changeset tools: the structured result the
// tool returns is also written, as JSON, to a file the caller names, so a
// script can read the exact result instead of having it retyped; the response
// is then only a digest of that result, unless verbose asks for both. The
// path is checked before any work is done, so a bad one fails cleanly up
// front. The report never changes the outcome of the tool: should the write
// fail after the work is done, the failure is noted in the result's log.
// ---------------------------------------------------------------------------

/// <summary>A validated reportPath request: where to write, whether to replace, and whether the response stays verbose.</summary>
public sealed record ReportRequest(string FullPath, bool Replace, bool Verbose = false);

public static class ReportFile
{
    static readonly JsonSerializerOptions Indented = new(CallToolResults.JsonOptions) { WriteIndented = true };

    /// <summary>
    /// True with <paramref name="request"/> null when no report was asked for; false with an error naming the
    /// argument when a value cannot be read, or the path cannot be used or already exists and overwrite is not true.
    /// </summary>
    public static bool TryRead(
        JsonElement reportPath, JsonElement overwrite, JsonElement verbose, out ReportRequest? request, out string? error)
    {
        request = null;
        if (!ToolArguments.TryReadOptionalString(reportPath, "reportPath", out string? path, out error)) return false;
        if (!ToolArguments.TryReadOptionalBool(overwrite, "overwrite", out bool replace, out error)) return false;
        if (!ToolArguments.TryReadOptionalBool(verbose, "verbose", out bool full, out error)) return false;
        if (path is null) return true;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "reportPath must not be blank.";
            return false;
        }

        string fullPath = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            error = $"reportPath {fullPath}: its folder does not exist.";
            return false;
        }
        if (File.Exists(fullPath) && !replace)
        {
            error = $"reportPath {fullPath} already exists; pass overwrite: true to replace it.";
            return false;
        }

        request = new ReportRequest(fullPath, replace, full);
        return true;
    }

    /// <summary>Writes <paramref name="result"/> as indented JSON, exactly as the tool serializes it; false with the reason when it cannot be written.</summary>
    public static bool TryWrite(object result, ReportRequest request, out string? error) =>
        ChangesetFileWriter.TryWrite(
            request.FullPath, JsonSerializer.SerializeToUtf8Bytes(result, result.GetType(), Indented), request.Replace, out error) is not null;
}

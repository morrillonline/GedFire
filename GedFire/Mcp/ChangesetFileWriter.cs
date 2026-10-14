using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Writes a drafted changeset to disk. The bytes are produced first so the
// hash a caller is told is the hash of exactly what was written.
// ---------------------------------------------------------------------------

static class ChangesetFileWriter
{
    public static byte[] Serialize(JsonObject changeset)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            changeset.WriteTo(writer);
        return stream.ToArray();
    }

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Writes to a new file, or replaces an existing one when <paramref name="replace"/>; null with an error naming the cause otherwise.</summary>
    public static string? TryWrite(string fullPath, byte[] bytes, bool replace, out string? error)
    {
        error = null;
        try
        {
            using var stream = new FileStream(fullPath, replace ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes);
            return fullPath;
        }
        catch (IOException) when (File.Exists(fullPath) && !replace)
        {
            error = $"{fullPath} already exists; pass overwrite: true to replace it.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"Could not write {fullPath}: {ex.Message}";
        }
        return null;
    }
}

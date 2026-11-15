using System.Buffers;
using System.Text;

namespace GedCore;

/// <summary>One stretch of garbled text and what it reads once repaired.</summary>
public sealed record GarbledTextRepair(string Garbled, string Repaired);

/// <summary>
/// Finds UTF-8 text that was read as Windows-1252 (or Latin-1), once or more: "â€¦", or "Ã¢â‚¬Â¦" when garbled twice,
/// for "…". Each garbled character is a UTF-8 lead byte (shown as Â…ô) followed by continuation bytes (shown as
/// € ‚ … ¦ ©, U+0080-U+00BF and the like), so text without a character in Â…ô is passed over in one vectorized scan.
/// </summary>
public static class GarbledText
{
    const char FirstLead = '\u00C2';
    const char LastLead = '\u00F4';

    // Text garbled more than three times is not seen in practice; the bound keeps a pathological input cheap.
    const int MaxRounds = 3;

    const int MaxShown = 60;

    /// <summary>The first-to-last garbled stretch of <paramref name="text"/> and its repair, or null when the text is clean.</summary>
    public static GarbledTextRepair? Find(string? text)
    {
        if (text is null) return null;
        string repaired = text;
        for (int round = 0; round < MaxRounds && RepairOnce(repaired) is { } next; round++)
            repaired = next;
        return ReferenceEquals(repaired, text) ? null : Difference(text, repaired);
    }

    /// <summary>The refusal for a garbled <paramref name="field"/>, quoting the garbled stretch and its repair.</summary>
    public static string Describe(string field, GarbledTextRepair repair) =>
        $"{field} has garbled characters \"{repair.Garbled}\" (UTF-8 text misread as Windows-1252); " +
        $"it should read \"{repair.Repaired}\".";

    static string? RepairOnce(string text)
    {
        StringBuilder? repaired = null;
        int copied = 0;
        int i = text.AsSpan().IndexOfAnyInRange(FirstLead, LastLead);
        while (i >= 0)
        {
            if (TryDecodeAt(text, i, out var rune, out int length)
                && (IsLatinLead(text[i]) || copied == i && repaired is not null || TryDecodeAt(text, i + length, out _, out _)))
            {
                repaired ??= new StringBuilder(text.Length);
                repaired.Append(text, copied, i - copied);
                Span<char> chars = stackalloc char[2];
                repaired.Append(chars[..rune.EncodeToUtf16(chars)]);
                i += length;
                copied = i;
            }
            else i++;

            int next = text.AsSpan(i).IndexOfAnyInRange(FirstLead, LastLead);
            i = next < 0 ? -1 : i + next;
        }
        if (repaired is null) return null;
        return repaired.Append(text, copied, text.Length - copied).ToString();
    }

    // Â Ã Ä Å decode to Latin-1 and Latin Extended-A, and à…ô need two or three continuation characters, so either is
    // garbled text on its own. Æ…ß before one symbol is too often real ("CAFÉ»", "Straße" and a no-break space, "Ø°"),
    // so it counts only beside another garbled character, as in a garbled Cyrillic or Greek word.
    static bool IsLatinLead(char lead) => lead <= '\u00C5' || lead >= '\u00E0';

    // A control character is not accepted as a repair: "Â" before a C1 control is not a garbled character worth restoring.
    static bool TryDecodeAt(string text, int start, out Rune rune, out int length)
    {
        rune = default;
        length = 0;
        if (start >= text.Length) return false;
        char lead = text[start];
        if (lead is < FirstLead or > LastLead) return false;
        length = lead <= '\u00DF' ? 2 : lead <= '\u00EF' ? 3 : 4;
        if (start + length > text.Length) return false;

        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)lead;
        for (int k = 1; k < length; k++)
        {
            int b = ByteOf(text[start + k]);
            if (b is < 0x80 or > 0xBF) return false;
            bytes[k] = (byte)b;
        }
        return Rune.DecodeFromUtf8(bytes[..length], out rune, out int consumed) == OperationStatus.Done
            && consumed == length && !Rune.IsControl(rune);
    }

    // The byte Windows-1252 shows as this character; Latin-1 and the C1 controls (cp1252's undefined bytes) map to themselves.
    static int ByteOf(char c) => c switch
    {
        <= '\u00FF' => c,
        '\u20AC' => 0x80, '\u201A' => 0x82, '\u0192' => 0x83, '\u201E' => 0x84, '\u2026' => 0x85, '\u2020' => 0x86,
        '\u2021' => 0x87, '\u02C6' => 0x88, '\u2030' => 0x89, '\u0160' => 0x8A, '\u2039' => 0x8B, '\u0152' => 0x8C,
        '\u017D' => 0x8E, '\u2018' => 0x91, '\u2019' => 0x92, '\u201C' => 0x93, '\u201D' => 0x94, '\u2022' => 0x95,
        '\u2013' => 0x96, '\u2014' => 0x97, '\u02DC' => 0x98, '\u2122' => 0x99, '\u0161' => 0x9A, '\u203A' => 0x9B,
        '\u0153' => 0x9C, '\u017E' => 0x9E, '\u0178' => 0x9F,
        _ => -1,
    };

    static GarbledTextRepair Difference(string text, string repaired)
    {
        int prefix = text.AsSpan().CommonPrefixLength(repaired);
        int maxSuffix = Math.Min(text.Length, repaired.Length) - prefix;
        int suffix = 0;
        while (suffix < maxSuffix && text[^(suffix + 1)] == repaired[^(suffix + 1)]) suffix++;
        // "Â£" repairs to "£", which also ends the garbled stretch; keep one shared character so the repair is not empty.
        if (prefix + suffix == repaired.Length)
        {
            if (suffix > 0) suffix--;
            else prefix--;
        }
        return new GarbledTextRepair(
            Shorten(text[prefix..^suffix]), Shorten(repaired[prefix..^suffix]));
    }

    static string Shorten(string stretch) => stretch.Length <= MaxShown ? stretch : stretch[..MaxShown] + "...";
}

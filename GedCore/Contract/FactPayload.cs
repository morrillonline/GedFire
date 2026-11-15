using GedCore.Apply;

namespace GedCore.Contract;

/// <summary>What a fact tag carries in the changeset dialect, fixed by its GEDCOM 7 payload.</summary>
public enum FactPayload
{
    /// <summary>An event whose payload is only Y or empty (BIRT, DEAT, CENS…): it takes date and place.</summary>
    Event,

    /// <summary>A text payload with no date or place (NAME, SEX): it takes value.</summary>
    Text,

    /// <summary>A text payload that is also dated and placed (OCCU, RESI, EVEN…): it takes value, date and place.</summary>
    Described,
}

public static class FactPayloads
{
    static readonly HashSet<string> Text = ["NAME", "SEX"];

    static readonly HashSet<string> Described =
        ["CAST", "DSCR", "EDUC", "IDNO", "NATI", "NCHI", "NMR", "OCCU", "PROP", "RELI", "RESI", "SSN", "TITL", "FACT", "EVEN"];

    /// <summary>The payload of a tag createOrUpdateVital lists; null for any other tag, such as an extension.</summary>
    public static FactPayload? Of(string? tag)
    {
        if (tag is null || !CreateOrUpdateVitalOp.Facts.Contains(tag)) return null;
        if (Text.Contains(tag)) return FactPayload.Text;
        return Described.Contains(tag) ? FactPayload.Described : FactPayload.Event;
    }

    /// <summary>Whether a text value is really a date put in the wrong field: NCHI's payload is a count, never a date.</summary>
    public static bool LooksLikeMisplacedDate(string tag, string? value) =>
        tag != "NCHI" && GedDateSyntax.IsValid(value);
}

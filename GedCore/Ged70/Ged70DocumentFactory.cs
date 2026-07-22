using System.Text.RegularExpressions;

namespace GedCore.Ged70;

/// <summary>Creates new GEDCOM 7 documents with the required seed person.</summary>
public static class Ged70DocumentFactory
{
    private static readonly Regex IndividualXref = new(@"^@I[A-Za-z0-9_]+@$", RegexOptions.Compiled);

    /// <summary>Create a minimal GEDCOM 7 document containing one named individual.</summary>
    public static GedDocument CreateSeeded(string name, string xref = "@I00001@", string? sex = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Seed person name is required.", nameof(name));
        if (!IndividualXref.IsMatch(xref))
            throw new ArgumentException("Seed person xref must have the form @I...@.", nameof(xref));
        if (sex is not null && sex is not ("M" or "F" or "X" or "U"))
            throw new ArgumentException("Seed person sex must be M, F, X, U, or omitted.", nameof(sex));

        var head = new GedRecord(0, null, "HEAD", "");
        var gedc = new GedRecord(1, null, "GEDC", "") { Parent = head };
        gedc.Children.Add(new GedRecord(2, null, "VERS", "7.0") { Parent = gedc });
        head.Children.Add(gedc);

        var person = new GedRecord(0, xref, "INDI", "");
        person.Children.Add(new GedRecord(1, null, "NAME", name) { Parent = person });
        if (sex is not null)
            person.Children.Add(new GedRecord(1, null, "SEX", sex) { Parent = person });
        person.Children.Add(new GedRecord(1, null, "UID", Guid.NewGuid().ToString()) { Parent = person });

        var document = new GedDocument([head, person, new GedRecord(0, null, "TRLR", "")]);
        document.RebuildXrefIndex();
        return document;
    }
}
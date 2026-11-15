namespace GedCore.Apply;

/// <summary>The relatives a parsed GEDCOM document records, read from its FAMC, FAMS, HUSB, WIFE and CHIL pointers.</summary>
public sealed class DocumentRelatives(GedDocument doc) : IExistingRelatives
{
    public Relative? Person(string xref) =>
        doc.ByXref.TryGetValue(xref, out var rec) && rec.Tag == "INDI"
            ? new Relative(xref, rec.FirstChild("NAME")?.FullValue() ?? "")
            : null;

    public IReadOnlyList<string> FamiliesAsChild(string person) => Pointers(person, "FAMC");

    public IReadOnlyList<string> FamiliesAsPartner(string person) => Pointers(person, "FAMS");

    public Relative? Partner(string family, string tag) =>
        Pointers(family, tag).Select(Person).FirstOrDefault(p => p is not null);

    public IReadOnlyList<Relative> Children(string family) => [.. Pointers(family, "CHIL").Select(Person).OfType<Relative>()];

    List<string> Pointers(string xref, string tag) =>
        doc.ByXref.TryGetValue(xref, out var rec)
            ? [.. rec.ChildrenByTag(tag).Where(c => c.IsPointerValue && !c.IsVoidPointer).Select(c => c.Value)]
            : [];
}

namespace GedCore.Drafting;

/// <summary>Finds sources the document already holds.</summary>
public interface ISourceCatalog
{
    /// <summary>The xref of an existing source with this title and author, or null.</summary>
    string? FindSource(string title, string? author);
}

/// <summary>Names the people the document already holds.</summary>
public interface IPersonNames
{
    /// <summary>The recorded name of a person, or null when the xref is not a known person.</summary>
    string? PersonName(string xref);
}

/// <summary>What the document already says about a record's facts and notes.</summary>
public interface IRecordHistory
{
    /// <summary>Every existing instance of a fact tag on a record, empty when there are none.</summary>

    IReadOnlyList<ExistingFact> FactsOn(string record, string tag);

    /// <summary>The text of every note already on a record.</summary>
    IReadOnlyList<string> NotesOn(string record);
}

/// <summary>Everything a draft needs to know about the document it will be applied to.</summary>
public interface IDraftLookup : ISourceCatalog, IPersonNames, IRecordHistory
{
    /// <summary>Whether the document lists the person as a child of the family; null when it cannot say (a record it does not hold, or a lookup that does not track families).</summary>
    bool? IsChildOf(string child, string family) => null;
}

/// <summary>One fact instance the document already holds, with the citations on it.</summary>
public sealed record ExistingFact(string? Date, string? Place, IReadOnlyList<ExistingCitation> Citations);

/// <summary>A citation the document already holds on a fact.</summary>
public sealed record ExistingCitation(string Source, string? Page, string? DataText, int? Quay);

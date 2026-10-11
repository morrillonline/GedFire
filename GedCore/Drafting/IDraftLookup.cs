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
    /// <summary>The citations already on a fact (an existing record's tag), empty when there are none.</summary>
    IReadOnlyList<ExistingCitation> CitationsOn(string record, string tag);

    /// <summary>The text of every note already on a record.</summary>
    IReadOnlyList<string> NotesOn(string record);
}

/// <summary>Everything a draft needs to know about the document it will be applied to.</summary>
public interface IDraftLookup : ISourceCatalog, IPersonNames, IRecordHistory;

/// <summary>A citation the document already holds on a fact.</summary>
public sealed record ExistingCitation(string Source, string? Page, string? DataText, int? Quay);

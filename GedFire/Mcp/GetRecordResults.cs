using System.Text.Json.Serialization;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// get_record's four result shapes, mirroring GetRecordTool.OutputSchemaJson
// property-for-property. Serialized with the shared
// CallToolResults.JsonOptions. Pure data — every mapping from a
// GedIndividual/GedFamily/GedSourceRecord to these records happens in
// GetRecordTool, not here.
// ---------------------------------------------------------------------------

public sealed record MediaFileDetail(string Path, string MediaType, string? Medium, string? Title, bool Resolved);

public sealed record CropDetail(int? Top, int? Left, int? Height, int? Width);

public sealed record MediaDetail(string Xref, string? Title, CropDetail? Crop, IReadOnlyList<MediaFileDetail> Files);

/// <summary>
/// One citation on a fact, note, or name. Kind is "citation" (a real source
/// citation), "inlineNote" (prose carried on the citation), or "personalNote"
/// (the personal-note pseudo-source, which cites nothing). Source is null only
/// for an inline note that names no source record.
/// </summary>
public sealed record CitationDetail(string Kind, string? Source, string? Page, string? DataText, int? Quay);

public sealed record EventDetail(
    string? Date,
    int? Year,
    string? Qualifier,
    string? Place,
    IReadOnlyList<CitationDetail> Citations,
    IReadOnlyList<MediaDetail> Media);

public sealed record NoteDetail(string Text, string? Mime, IReadOnlyList<CitationDetail> Citations);

public sealed record ChildIdentity(string Xref, string Name, int? BirthYear);

public sealed record ParentFamilyReference(string Xref, string? FatherName, string? MotherName);

public sealed record SpouseReference(string Xref, string Name);

public sealed record SpouseFamilyDetail(
    string Xref,
    string? SpouseName,
    EventDetail? Marriage,
    IReadOnlyList<ChildIdentity> Children);

public sealed record PersonRecord(
    string RecordType,
    string Xref,
    string Name,
    string? Title,
    string? Sex,
    EventDetail? Birth,
    EventDetail? Death,
    EventDetail? Will,
    EventDetail? Probate,
    IReadOnlyList<EventDetail> Census,
    IReadOnlyList<CitationDetail> NameCitations,
    IReadOnlyList<NoteDetail> Notes,
    string? Restriction,
    IReadOnlyList<MediaDetail> Media,
    ParentFamilyReference? FamilyAsChild,
    IReadOnlyList<SpouseFamilyDetail> FamiliesAsSpouse)
{
    /// <summary>Present only when get_record was asked to includeSources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SourceRecord>? Sources { get; init; }
}

public sealed record FamilyRecord(
    string RecordType,
    string Xref,
    SpouseReference? Husband,
    SpouseReference? Wife,
    EventDetail? Marriage,
    IReadOnlyList<ChildIdentity> Children,
    IReadOnlyList<MediaDetail> Media)
{
    /// <summary>Present only when get_record was asked to includeSources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SourceRecord>? Sources { get; init; }
}

/// <summary>One structure that cites a source: the record's xref and record type, and which field carries the citation.</summary>
public sealed record CitedByEntry(string Xref, string RecordType, string Field);

public sealed record SourceRecord(
    string RecordType,
    string Xref,
    string? Author,
    string? Title,
    string? Publication,
    string? Note)
{
    /// <summary>Present only on a direct lookup of the source, never on a source embedded by includeSources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CitedByEntry>? CitedBy { get; init; }
}

public sealed record NotFoundRecord(string RecordType, string Xref);

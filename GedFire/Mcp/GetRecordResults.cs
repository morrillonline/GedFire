namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// get_record's four result shapes, mirroring GetRecordTool.OutputSchemaJson
// property-for-property (docs/design/mcp-server.md "The third tool:
// get_record"). Serialized with the shared CallToolResults.JsonOptions.
// Pure data — every mapping from a GedIndividual/GedFamily/GedSourceRecord
// to these records happens in GetRecordTool, not here.
// ---------------------------------------------------------------------------

public sealed record MediaFileDetail(string Path, string MediaType, string? Medium, string? Title, bool Resolved);

public sealed record CropDetail(int? Top, int? Left, int? Height, int? Width);

public sealed record MediaDetail(string Xref, string? Title, CropDetail? Crop, IReadOnlyList<MediaFileDetail> Files);

public sealed record EventDetail(
    string? Date,
    int? Year,
    string? Qualifier,
    string? Place,
    IReadOnlyList<string> Sources,
    IReadOnlyList<MediaDetail> Media);

public sealed record NoteDetail(string Text, string? Mime, IReadOnlyList<string> Sources);

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
    IReadOnlyList<string> NameSources,
    IReadOnlyList<NoteDetail> Notes,
    string? Restriction,
    IReadOnlyList<MediaDetail> Media,
    ParentFamilyReference? FamilyAsChild,
    IReadOnlyList<SpouseFamilyDetail> FamiliesAsSpouse);

public sealed record FamilyRecord(
    string RecordType,
    string Xref,
    SpouseReference? Husband,
    SpouseReference? Wife,
    EventDetail? Marriage,
    IReadOnlyList<ChildIdentity> Children,
    IReadOnlyList<MediaDetail> Media);

public sealed record SourceRecord(
    string RecordType,
    string Xref,
    string? Author,
    string? Title,
    string? Publication,
    string? Note);

public sealed record NotFoundRecord(string RecordType, string Xref);

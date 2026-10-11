namespace GedCore.Points;

/// <summary>
/// A research card an author is working: the person it is about, its class
/// ("enrich-person", "new-parent", "new-spouse", "new-child"), for an enrich
/// card the gap it names ("enrich-death", "enrich-death-place"), and its points.
/// </summary>
public sealed record PointsCard(string Xref, string Class, string? Gap, string? Name, int Points)
{
    public string Key => Gap is null ? $"{Xref}|{Class}" : $"{Xref}|{Class}|{Gap}";
}

public sealed record ClosedCard(string CardKey, int Points, string Wrote);

public sealed record UnclosedCard(string CardKey, string Reason);

public sealed record SubjectMismatch(string CardKey, string CardSubject, string AppliedName);

public sealed record PointsPreview(
    IReadOnlyList<ClosedCard> Closed, IReadOnlyList<UnclosedCard> NotClosed,
    IReadOnlyList<SubjectMismatch> SubjectMismatch, int Total);

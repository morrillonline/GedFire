using System.Text.Json.Serialization;

namespace GedCore.Points;

/// <summary>
/// A research card an author is working: the person it is about, its class
/// ("enrich-person", "new-parent", "new-spouse", "new-child", "biography"), for an enrich
/// card the gap it names ("enrich-death", "enrich-death-place"), and its points.
/// </summary>
public sealed record PointsCard(string Xref, string Class, string? Gap, string? Name, int Points, int Bonus = 0)
{
    /// <summary>For a new-child card, the family it was dealt for; null leaves it to any childless family of the subject.</summary>
    public string? Family { get; init; }

    public string Key => Gap is null ? $"{Xref}|{Class}" : $"{Xref}|{Class}|{Gap}";
}

/// <summary><see cref="Family"/> repeats the card's own, so two New child cards of one person can be told apart.</summary>
public sealed record ClosedCard(string CardKey, int Points, string Wrote)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Family { get; init; }
}

/// <inheritdoc cref="ClosedCard"/>
public sealed record UnclosedCard(string CardKey, string Reason)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Family { get; init; }
}

public sealed record SubjectMismatch(string CardKey, string CardSubject, string AppliedName);

public sealed record PointsPreview(
    IReadOnlyList<ClosedCard> Closed, IReadOnlyList<UnclosedCard> NotClosed,
    IReadOnlyList<SubjectMismatch> SubjectMismatch, int Total);

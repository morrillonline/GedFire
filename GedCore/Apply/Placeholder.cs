using System.Text.RegularExpressions;

namespace GedCore.Apply;

// ---------------------------------------------------------------------------
// Reserved placeholder xrefs: a caller names a new record @New<token>@
// instead of picking its real xref; apply mints
// and commits the real identity. A placeholder is never a real document
// xref and is never treated as one -- GedFire's own real xrefs always take
// the form @<Kind><digits>@, so the New prefix never collides with a real
// record's own identity except the one documented edge case (a foreign
// import whose literal xref happens to match the pattern), which apply
// checks for before minting (see XrefMinter).
// ---------------------------------------------------------------------------

/// <summary>Which kind of record a placeholder token names, fixed by its first creating op.</summary>
public enum PlaceholderKind { Person, Family, Source, Media }

internal static class Placeholder
{
    // <token> is any non-empty run of letters, digits, or underscores.
    static readonly Regex Pattern = new(@"^@New[A-Za-z0-9_]+@$", RegexOptions.Compiled);

    public static bool IsPlaceholder(string? xref) => xref is not null && Pattern.IsMatch(xref);

    /// <summary>
    /// The one collision the design can't statically rule out: a document
    /// that already contains a real record whose literal xref happens to
    /// match @New&lt;token&gt;@ -- most plausibly a
    /// foreign import that used an unrelated naming scheme. A placeholder
    /// must never be treated as referring to that record, whether the
    /// occurrence is an inline creation, a name-mismatched collision, or a
    /// bare reference that would otherwise silently degrade to a link — so
    /// this is checked before any of those branches run, not folded into
    /// the ordinary existing-record comparison.
    /// </summary>
    public static string? RejectRealRecordCollision(ResolutionContext ctx, string xref) =>
        IsPlaceholder(xref) && ctx.Existing(xref) is not null
            ? $"{xref} is a reserved placeholder shape, but the document already contains a real " +
              "record with that literal xref — a document health problem to fix at the source, not " +
              "something this changeset can resolve"
            : null;
}

/// <summary>
/// One placeholder's fixed kind and (for a person placeholder only) the
/// inline identity evidence gathered so far, for cross-occurrence conflict
/// detection: a later creation-capable occurrence contributes compatible
/// inline evidence to the same planned record, while conflicting record
/// kind or inline identity fields are validation errors.
/// </summary>
internal sealed record PlaceholderEntry(PlaceholderKind Kind, string? Name, string? Sex);

/// <summary>
/// The changeset-wide placeholder registry: exactly one entry per token,
/// shared by every op's validation within one selected changeset.
/// <see cref="Register"/> is called by both the first (creating)
/// occurrence and any later creation-capable occurrence of the same token;
/// a plain reference occurrence (no inline evidence to contribute) should
/// call <see cref="TryGetKind"/> instead.
/// </summary>
internal sealed class PlaceholderRegistry
{
    readonly Dictionary<string, PlaceholderEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGetKind(string xref, out PlaceholderKind kind)
    {
        if (_entries.TryGetValue(xref, out var entry)) { kind = entry.Kind; return true; }
        kind = default;
        return false;
    }

    /// <summary>Every placeholder token registered as a Person, in first-registration order.</summary>
    public IEnumerable<string> PersonTokens() =>
        _entries.Where(e => e.Value.Kind == PlaceholderKind.Person).Select(e => e.Key);

    /// <summary>
    /// Register a placeholder occurrence that creates or names inline
    /// identity for <paramref name="xref"/>. The first call for a token
    /// fixes its kind. A later call for the same token must agree on kind;
    /// for a Person placeholder, a non-null <paramref name="name"/> or
    /// <paramref name="sex"/> that conflicts with a previously recorded
    /// value is also an error. Returns the conflict message, or null when
    /// the registration succeeded (including a compatible repeat).
    /// </summary>
    public string? Register(string xref, PlaceholderKind kind, string? name = null, string? sex = null)
    {
        if (_entries.TryGetValue(xref, out var existing))
        {
            if (existing.Kind != kind)
                return $"placeholder {xref} was already used to create a {existing.Kind}; " +
                       $"it cannot also name a {kind}";
            if (name is not null && existing.Name is not null && existing.Name != name)
                return $"placeholder {xref}: conflicting name ('{existing.Name}' vs '{name}')";
            if (sex is not null && existing.Sex is not null && existing.Sex != sex)
                return $"placeholder {xref}: conflicting sex ('{existing.Sex}' vs '{sex}')";
            _entries[xref] = existing with { Name = existing.Name ?? name, Sex = existing.Sex ?? sex };
            return null;
        }
        _entries[xref] = new PlaceholderEntry(kind, name, sex);
        return null;
    }
}

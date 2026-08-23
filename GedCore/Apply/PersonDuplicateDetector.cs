using GedCore.Matching;

namespace GedCore.Apply;

// New-person duplicate detection: for every person-creation placeholder in
// the selected changeset, gather the evidence the changeset itself supplies,
// score it with the same PersonMatchCore find_person uses, and reject the
// whole changeset before any mutation on a high-confidence existing match.
// Runs once every op's Validate has populated ResolutionContext.Placeholders,
// so evidence from ops after the creating PersonRef is seen too.

internal static class PersonDuplicateDetector
{
    // A confident single match at or above this score is treated as the same
    // person, not a namesake.
    const double DuplicateScoreFloor = 90.0;

    static readonly Lazy<NicknameDirectory> Nicknames = new(NicknameDirectory.LoadEmbedded);
    static readonly PersonMatchCore MatchCore = new();

    /// <summary>
    /// Append one error per person-creation placeholder that resolves to a
    /// high-confidence existing match, or that carries conflicting scalar
    /// birth evidence across occurrences. Read-only: never mutates
    /// <paramref name="doc"/>.
    /// </summary>
    public static void Check(
        GedDocument doc, ResolutionContext ctx, IReadOnlyList<ChangeOp> selectedOps, List<string> errors)
    {
        var tokens = ctx.Placeholders.PersonTokens().ToList();
        if (tokens.Count == 0) return;

        var evidence = new Dictionary<string, PersonEvidence>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            var (gathered, conflict) = GatherEvidence(ctx, selectedOps, token);
            if (conflict is not null) { errors.Add(conflict); continue; }
            evidence[token] = gathered;
        }
        if (evidence.Count == 0) return;

        var realCandidates = PersonRecordIndex.Build(doc);

        foreach (var (token, self) in evidence)
        {
            var provisional = evidence
                .Where(kv => kv.Key != token)
                .Select(kv => PersonRecordIndex.ToProvisionalCandidate(kv.Key, kv.Value));
            var candidates = realCandidates.Concat(provisional).ToList();

            var hints = new MatchHints(self.BirthYear, self.Place, self.SpouseName, self.ParentName);
            var outcome = MatchCore.Match(candidates, self.Name, hints, Nicknames.Value, maxResults: 1);

            if (outcome.PersonMatchType == PersonMatchType.Single && outcome.Matches[0].FinalScore >= DuplicateScoreFloor)
            {
                var match = outcome.Matches[0];
                errors.Add(
                    $"{token}: a high-confidence match already exists for \"{self.Name}\" — {match.Id} " +
                    $"(score {match.FinalScore:0.0}); remove the creation request or reference the " +
                    "existing person instead of creating a new one");
            }
        }
    }

    // Evidence gathering: the inline name is the query; a supplied birth
    // fact contributes year/place; relationship ops collect spouse/parent
    // names that resolve in the pre-apply document, kept only when exactly
    // one distinct name was found for that slot.

    static (PersonEvidence Evidence, string? ConflictError) GatherEvidence(
        ResolutionContext ctx, IReadOnlyList<ChangeOp> selectedOps, string token)
    {
        string? name = null;
        string? sex = null;
        int? birthYear = null;
        string? birthPlace = null;
        var spouseNames = new HashSet<string>(StringComparer.Ordinal);
        var parentNames = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = new List<string>();

        void ObserveBirth(IReadOnlyList<InlineFact> facts)
        {
            foreach (var fact in facts.Where(f => f.Fact == "BIRT"))
            {
                int parsedYear = GedDate.ParseYear(fact.Value?.Date);
                if (parsedYear != 0)
                {
                    if (birthYear is int existingYear && existingYear != parsedYear)
                        conflicts.Add($"{token}: conflicting birth year in this changeset " +
                                       $"('{existingYear}' vs '{parsedYear}')");
                    birthYear ??= parsedYear;
                }
                string? place = fact.Value?.Place;
                if (!string.IsNullOrWhiteSpace(place))
                {
                    if (birthPlace is string existingPlace && existingPlace != place)
                        conflicts.Add($"{token}: conflicting birth place in this changeset " +
                                       $"('{existingPlace}' vs '{place}')");
                    birthPlace ??= place;
                }
            }
        }

        void ObservePersonRef(PersonRef p)
        {
            if (p.Xref != token) return;
            name ??= p.Name;
            sex ??= p.Sex;
            ObserveBirth(p.Facts);
        }

        // A name that resolves in the pre-apply document only -- a sibling
        // placeholder is not itself evidence (its own evidence is gathered
        // and represented as a separate provisional candidate instead).
        string? ResolveRealName(string? xref) =>
            xref is not null && !Placeholder.IsPlaceholder(xref) && ctx.Existing(xref) is { } rec
                ? rec.FirstChild("NAME")?.FullValue()
                : null;

        void AddSpouse(string? realName)
        {
            if (!string.IsNullOrWhiteSpace(realName))
                spouseNames.Add(PersonNameNormalizer.Normalize(realName));
        }

        void AddParent(string? realName)
        {
            if (!string.IsNullOrWhiteSpace(realName))
                parentNames.Add(PersonNameNormalizer.Normalize(realName));
        }

        foreach (var op in selectedOps)
        {
            switch (op)
            {
                case CreateOrUpdateSpouseOp spouseOp:
                    ObservePersonRef(spouseOp.Spouse);
                    if (spouseOp.Spouse.Xref == token)
                        AddSpouse(ResolveRealName(spouseOp.Person));
                    if (spouseOp.Person == token)
                        AddSpouse(spouseOp.Spouse.IsInline ? null : ResolveRealName(spouseOp.Spouse.Xref));
                    break;

                case CreateOrUpdateChildOp childOp:
                    ObservePersonRef(childOp.Child);
                    if (childOp.Child.Xref == token)
                    {
                        if (childOp.Husb is not null || childOp.Wife is not null)
                        {
                            AddParent(ResolveRealName(childOp.Husb));
                            AddParent(ResolveRealName(childOp.Wife));
                        }
                        else if (!Placeholder.IsPlaceholder(childOp.Family) && ctx.Existing(childOp.Family) is { } fam)
                        {
                            AddParent(ResolveRealName(fam.FirstChild("HUSB")?.Value));
                            AddParent(ResolveRealName(fam.FirstChild("WIFE")?.Value));
                        }
                    }
                    break;

                case CreateOrUpdateParentOp parentOp:
                    ObservePersonRef(parentOp.Parent);
                    if (parentOp.Parent.Xref == token)
                    {
                        GedRecord? fam = parentOp.Family is { } f && !Placeholder.IsPlaceholder(f)
                            ? ctx.Existing(f)
                            : null;
                        if (fam is null && !Placeholder.IsPlaceholder(parentOp.Person)
                            && ctx.Existing(parentOp.Person) is { } childRec)
                        {
                            var famcs = childRec.ChildrenByTag("FAMC").Select(f => f.Value).ToList();
                            if (famcs.Count == 1) fam = ctx.Existing(famcs[0]);
                        }
                        string otherRoleTag = parentOp.Role == "father" ? "WIFE" : "HUSB";
                        AddSpouse(ResolveRealName(fam?.FirstChild(otherRoleTag)?.Value));
                    }
                    break;

                // A later op in the same changeset can also assert the new
                // person's birth fact, not only the creating PersonRef's own.
                case CreateOrUpdateVitalOp { Fact: "BIRT" } vitalOp when vitalOp.Record == token:
                    ObserveBirth([new InlineFact("BIRT", vitalOp.Value, [])]);
                    break;
            }
        }

        if (conflicts.Count > 0)
            return (default, string.Join("; ", conflicts));

        // Two named parents (or two spouses) are valid evidence about a real
        // person, not conflicting values -- omit the hint rather than guess
        // which one to keep, since find_person's request has one slot each.
        string? spouseHint = spouseNames.Count == 1 ? spouseNames.Single() : null;
        string? parentHint = parentNames.Count == 1 ? parentNames.Single() : null;

        return (new PersonEvidence(name ?? "", sex, birthYear, birthPlace, spouseHint, parentHint), null);
    }
}

/// <summary>
/// One person-creation placeholder's gathered duplicate-detection evidence.
/// <see cref="SpouseName"/>/<see cref="ParentName"/> are already normalized
/// (<see cref="PersonNameNormalizer"/>); <see cref="Place"/> is raw free text,
/// normalized the same way <see cref="MatchHints"/> place hints always are.
/// </summary>
internal readonly record struct PersonEvidence(
    string Name, string? Sex, int? BirthYear, string? Place, string? SpouseName, string? ParentName);

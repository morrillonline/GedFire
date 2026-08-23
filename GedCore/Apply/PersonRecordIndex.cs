using GedCore.Matching;

namespace GedCore.Apply;

// Adapts GedCore.Apply's raw GedRecord data to GedCore.Matching's neutral
// PersonMatchCandidate shape. GedCore.Apply-side twin of GedFire.Match.MatchIndex,
// which extracts the same shape from GedIndividual/GedModel for find_person —
// keep the two in step.

internal static class PersonRecordIndex
{
    /// <summary>Every real INDI record in <paramref name="doc"/>, as neutral match candidates.</summary>
    public static List<PersonMatchCandidate> Build(GedDocument doc)
    {
        var individuals = doc.Records.Where(r => r.Tag == "INDI").ToList();
        var families = doc.Records.Where(r => r.Tag == "FAM").ToList();
        var byXref = individuals
            .Where(r => r.Xref is not null)
            .ToDictionary(r => r.Xref!, r => r, StringComparer.Ordinal);

        string? NameOf(string? xref) =>
            xref is not null && byXref.TryGetValue(xref, out var rec) ? rec.FirstChild("NAME")?.FullValue() : null;

        var list = new List<PersonMatchCandidate>(individuals.Count);
        foreach (var indi in individuals)
        {
            if (indi.Xref is null) continue;
            var (given, surname) = SplitName(indi.FirstChild("NAME")?.FullValue());

            PersonMatchEvent? birth = EventOf(indi.ChildrenByTag("BIRT").LastOrDefault());
            PersonMatchEvent? death = EventOf(indi.ChildrenByTag("DEAT").LastOrDefault());

            string? famcXref = indi.ChildrenByTag("FAMC").LastOrDefault()?.Value;
            var famc = famcXref is not null ? families.FirstOrDefault(f => f.Xref == famcXref) : null;
            PersonMatchParents? parents = null;
            if (famc is not null)
            {
                string? father = NormalizeOrNull(NameOf(famc.FirstChild("HUSB")?.Value));
                string? mother = NormalizeOrNull(NameOf(famc.FirstChild("WIFE")?.Value));
                if (father is not null || mother is not null)
                    parents = new PersonMatchParents(father, mother);
            }

            var marriages = new List<PersonMatchMarriage>();
            foreach (var familyLink in indi.ChildrenByTag("FAMS"))
            {
                if (familyLink.Value == GedRecord.VoidPointer) continue;
                var family = families.FirstOrDefault(f => f.Xref == familyLink.Value);
                if (family is null) continue;

                string? husband = family.FirstChild("HUSB")?.Value;
                string? wife = family.FirstChild("WIFE")?.Value;
                string? spouseXref = husband == indi.Xref ? wife : wife == indi.Xref ? husband : null;
                string? spouseName = NormalizeOrNull(NameOf(spouseXref));
                var marriage = family.ChildrenByTag("MARR").LastOrDefault();
                var marriageEvent = EventOf(marriage);
                marriages.Add(new PersonMatchMarriage(
                    spouseName,
                    marriageEvent?.Year,
                    marriageEvent?.NormalizedPlace));
            }

            list.Add(new PersonMatchCandidate(
                indi.Xref,
                DisplayName(given, surname),
                PersonNameNormalizer.Normalize(surname),
                PersonNameNormalizer.Normalize(given),
                SexOf(indi),
                birth,
                death,
                parents,
                marriages));
        }
        return list;
    }

    /// <summary>
    /// One person-creation placeholder's gathered evidence, represented as a
    /// provisional candidate another placeholder's duplicate check can be
    /// scored against.
    /// </summary>
    public static PersonMatchCandidate ToProvisionalCandidate(string token, PersonEvidence evidence)
    {
        var (given, surname) = SplitName(evidence.Name);
        bool? isMale = evidence.Sex switch { "M" => true, "F" => false, _ => null };
        string? birthPlace = NormalizeOrNull(evidence.BirthPlace);
        PersonMatchEvent? birth = evidence.BirthYear is not null || birthPlace is not null
            ? new PersonMatchEvent(evidence.BirthYear, birthPlace)
            : null;
        PersonMatchParents? parents = evidence.FatherName is not null || evidence.MotherName is not null
            ? new PersonMatchParents(evidence.FatherName, evidence.MotherName)
            : null;
        IReadOnlyList<PersonMatchMarriage> marriages = evidence.SpouseName is string spouseName
            ? [new PersonMatchMarriage(spouseName, null, null)]
            : [];

        return new PersonMatchCandidate(
            token,
            DisplayName(given, surname),
            PersonNameNormalizer.Normalize(surname),
            PersonNameNormalizer.Normalize(given),
            isMale,
            birth,
            null,
            parents,
            marriages);
    }

    // GedNamePayload.Split (GedCore) owns the slash tokenizing; when there is
    // no slash at all this candidate index's own longstanding policy treats
    // the whole payload as a given name with an empty surname, unchanged.
    static (string GivenMiddle, string Surname) SplitName(string? rawName)
    {
        var (givenMiddle, surname) = GedNamePayload.Split(rawName);
        return (givenMiddle, surname ?? "");
    }

    static string DisplayName(string givenMiddle, string surname) => (givenMiddle + " " + surname).Trim();

    static bool? SexOf(GedRecord indi) => indi.FirstChild("SEX")?.Value switch
    {
        "M" => true,
        "F" => false,
        _ => null,
    };

    static PersonMatchEvent? EventOf(GedRecord? gedEvent)
    {
        if (gedEvent is null) return null;
        int parsedYear = GedDate.ParseYear(gedEvent.ChildrenByTag("DATE").LastOrDefault()?.FullValue());
        int? year = parsedYear != 0 ? parsedYear : null;
        string? place = NormalizeOrNull(gedEvent.ChildrenByTag("PLAC").LastOrDefault()?.FullValue());
        return year is null && place is null ? null : new PersonMatchEvent(year, place);
    }

    static string? NormalizeOrNull(string? value)
    {
        string normalized = PersonNameNormalizer.Normalize(value);
        return normalized.Length > 0 ? normalized : null;
    }
}

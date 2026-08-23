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

            var places = new List<string>();
            AddNormalized(places, indi.FirstChild("BIRT")?.FirstChild("PLAC")?.Value);
            AddNormalized(places, indi.FirstChild("DEAT")?.FirstChild("PLAC")?.Value);
            foreach (var census in indi.ChildrenByTag("CENS"))
                AddNormalized(places, census.FirstChild("PLAC")?.Value);

            var spouseNames = new List<string>();
            foreach (var fam in families)
            {
                string? husb = fam.FirstChild("HUSB")?.Value;
                string? wife = fam.FirstChild("WIFE")?.Value;
                string? otherXref = husb == indi.Xref ? wife : wife == indi.Xref ? husb : null;
                if (otherXref is null) continue;
                AddNormalized(spouseNames, NameOf(otherXref));
            }

            var parentNames = new List<string>();
            // Matches ModelBuilder's parent-family resolution: last FAMC wins.
            string? famcXref = indi.ChildrenByTag("FAMC").LastOrDefault()?.Value;
            var famc = famcXref is not null ? families.FirstOrDefault(f => f.Xref == famcXref) : null;
            if (famc is not null)
            {
                AddNormalized(parentNames, NameOf(famc.FirstChild("HUSB")?.Value));
                AddNormalized(parentNames, NameOf(famc.FirstChild("WIFE")?.Value));
            }

            list.Add(new PersonMatchCandidate(
                indi.Xref,
                DisplayName(given, surname),
                PersonNameNormalizer.Normalize(surname),
                PersonNameNormalizer.Normalize(given),
                BirthYearOf(indi),
                SexOf(indi),
                places,
                spouseNames,
                parentNames));
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
        List<string> places = [];
        AddNormalized(places, evidence.Place);
        // SpouseName/ParentName are already normalized by the evidence gatherer.
        List<string> spouses = evidence.SpouseName is string s ? [s] : [];
        List<string> parents = evidence.ParentName is string p ? [p] : [];

        return new PersonMatchCandidate(
            token,
            DisplayName(given, surname),
            PersonNameNormalizer.Normalize(surname),
            PersonNameNormalizer.Normalize(given),
            evidence.BirthYear,
            isMale,
            places,
            spouses,
            parents);
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

    static int? BirthYearOf(GedRecord indi)
    {
        int year = GedDate.ParseYear(indi.FirstChild("BIRT")?.FirstChild("DATE")?.FullValue());
        return year != 0 ? year : null;
    }

    static void AddNormalized(List<string> values, string? raw)
    {
        string normalized = PersonNameNormalizer.Normalize(raw);
        if (normalized.Length > 0) values.Add(normalized);
    }
}

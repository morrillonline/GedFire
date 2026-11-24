using System.Text.RegularExpressions;
using GedCore;

namespace GedFire.Gen;

/// <summary>
/// The words a generated page shows for a fact: the heading of its row in a facts list, the word that names it in
/// running prose, and the clauses its substructures (age, cause, agency…) add.
/// </summary>
public static partial class FactText
{
    static readonly Dictionary<string, string> Headings = new(StringComparer.Ordinal)
    {
        ["BIRT"] = "Born", ["CHR"] = "Christened", ["BAPM"] = "Baptized", ["BLES"] = "Blessed", ["ADOP"] = "Adopted",
        ["BARM"] = "Bar mitzvah", ["BASM"] = "Bas mitzvah", ["CONF"] = "Confirmed", ["FCOM"] = "First communion",
        ["CHRA"] = "Adult christening", ["GRAD"] = "Graduated", ["EMIG"] = "Emigrated", ["IMMI"] = "Immigrated",
        ["NATU"] = "Naturalized", ["ORDN"] = "Ordained", ["RETI"] = "Retired", ["CENS"] = "Census", ["RESI"] = "Resided",
        ["DEAT"] = "Died", ["BURI"] = "Buried", ["CREM"] = "Cremated", ["WILL"] = "Will", ["PROB"] = "Probate",
        ["EVEN"] = "Event", ["FACT"] = "Fact", ["IDNO"] = "ID number",
        ["CAST"] = "Caste", ["DSCR"] = "Description", ["EDUC"] = "Education", ["NATI"] = "Nationality",
        ["NCHI"] = "Children", ["NMR"] = "Marriages", ["OCCU"] = "Occupation", ["PROP"] = "Property",
        ["RELI"] = "Religion", ["SSN"] = "Social Security no.", ["TITL"] = "Title",
        ["ENGA"] = "Engaged", ["MARB"] = "Banns", ["MARC"] = "Marriage contract", ["MARL"] = "Marriage license",
        ["MARR"] = "Married", ["MARS"] = "Marriage settlement", ["DIVF"] = "Divorce filed", ["DIV"] = "Divorced",
        ["ANUL"] = "Annulled",
    };

    // Facts whose TYPE says what kind of fact they are, so it names them in place of the tag's word.
    static readonly HashSet<string> TypedTags = ["EVEN", "FACT", "IDNO"];

    /// <summary>The heading of the fact's row: "Occupation", "Baptized", or an EVEN's own TYPE.</summary>
    public static string Heading(GedEvent fact) =>
        TypeName(fact) ?? Headings.GetValueOrDefault(fact.Tag) ?? Capitalized(FactLabel.Of(fact.Tag));

    /// <summary>The word that names the fact in running prose: "occupation", "divorce", or an EVEN's own TYPE.</summary>
    public static string Word(GedEvent fact) => TypeName(fact) ?? FactLabel.Of(fact.Tag);

    /// <summary>The clauses the fact's substructures add, in document order: "age: 49 years", "cause: Pneumonia".</summary>
    public static IReadOnlyList<string> Details(GedEvent fact) =>
        [.. fact.Details.Where(d => !(d.Tag == "TYPE" && TypeName(fact) is not null)).Select(Clause)];

    static string? TypeName(GedEvent fact) =>
        TypedTags.Contains(fact.Tag) && fact.DetailOf("TYPE") is { Length: > 0 } type ? type : null;

    static string Clause(GedEventDetail detail) => detail.Tag switch
    {
        "TYPE" => "type: " + detail.Value,
        "AGE"  => "age: " + Age(detail.Value),
        "HUSB" => "husband's age: " + Age(detail.Value),
        "WIFE" => "wife's age: " + Age(detail.Value),
        "CAUS" => "cause: " + detail.Value,
        "AGNC" => "agency: " + detail.Value,
        "RELI" => "religion: " + detail.Value,
        "ADDR" => "address: " + string.Join(", ", detail.Value.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
        _      => detail.Value,
    };

    /// <summary>A GEDCOM age in words: "&gt; 2y 3m" reads "more than 2 years 3 months"; anything else is kept as written.</summary>
    public static string Age(string age)
    {
        var match = AgePattern().Match(age.Trim());
        if (!match.Success) return age.Trim();

        var words = new List<string>();
        if (match.Groups["bound"].Value == ">") words.Add("more than");
        if (match.Groups["bound"].Value == "<") words.Add("less than");
        foreach (Capture part in match.Groups["part"].Captures)
        {
            int count = int.Parse(part.Value[..^1]);
            string unit = part.Value[^1] switch { 'y' => "year", 'm' => "month", 'w' => "week", _ => "day" };
            words.Add($"{count} {unit}{(count == 1 ? "" : "s")}");
        }
        return string.Join(' ', words);
    }

    [GeneratedRegex(@"^(?<bound>[<>])?\s*(?:(?<part>\d+[ymwd])\s*)+$")]
    private static partial Regex AgePattern();

    static string Capitalized(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}

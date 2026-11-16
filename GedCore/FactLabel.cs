namespace GedCore;

/// <summary>
/// The word for each fact a record can carry, as prose names it: "occupation" for OCCU, "residence" for RESI. Text a
/// person reads, such as a note written into the tree, names a fact by its word, never by its GEDCOM tag.
/// </summary>
public static class FactLabel
{
    static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["NAME"] = "name", ["SEX"] = "sex",
        ["ADOP"] = "adoption", ["BAPM"] = "baptism", ["BARM"] = "bar mitzvah", ["BASM"] = "bas mitzvah", ["BIRT"] = "birth",
        ["BLES"] = "blessing", ["BURI"] = "burial", ["CENS"] = "census entry", ["CHR"] = "christening",
        ["CHRA"] = "adult christening", ["CONF"] = "confirmation", ["CREM"] = "cremation", ["DEAT"] = "death",
        ["EMIG"] = "emigration", ["FCOM"] = "first communion", ["GRAD"] = "graduation", ["IMMI"] = "immigration",
        ["NATU"] = "naturalization", ["ORDN"] = "ordination", ["PROB"] = "probate", ["RETI"] = "retirement", ["WILL"] = "will",
        ["EVEN"] = "event",
        ["CAST"] = "caste", ["DSCR"] = "physical description", ["EDUC"] = "education", ["IDNO"] = "identification number",
        ["NATI"] = "nationality", ["NCHI"] = "number of children", ["NMR"] = "number of marriages", ["OCCU"] = "occupation",
        ["PROP"] = "property", ["RELI"] = "religion", ["RESI"] = "residence", ["SSN"] = "Social Security number",
        ["TITL"] = "title", ["FACT"] = "fact",
        ["ANUL"] = "annulment", ["DIV"] = "divorce", ["DIVF"] = "divorce filing", ["ENGA"] = "engagement",
        ["MARB"] = "marriage banns", ["MARC"] = "marriage contract", ["MARL"] = "marriage license", ["MARR"] = "marriage",
        ["MARS"] = "marriage settlement",
    };

    /// <summary>Every tag that has a word, with its word.</summary>
    public static IReadOnlyDictionary<string, string> All => Words;

    /// <summary>The word for <paramref name="tag"/>; for a tag with no word (an extension), "fact" rather than the tag.</summary>
    public static string Of(string tag) => Words.GetValueOrDefault(tag.ToUpperInvariant(), "fact");
}

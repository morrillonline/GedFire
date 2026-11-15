namespace GedCore.Tests;

public class GarbledTextTests
{
    [Theory]
    [InlineData("Élise Lefèvre née Brûlé, Françoise Leçon, naïve, Noël, Chloë")]                 // French
    [InlineData("« CAF\u00C9\u00A0» and «CAFÉ» and l'ÉTÉ\u00A0!")]                                  // French typography
    [InlineData("Jürgen Schröder, Größe, Straße\u00A0Nr. 5, MÜLLER, Äpfel")]                        // German
    [InlineData("José María Núñez Peña, Muñoz, ¿Qué?, ¡Olé!, 1.ª, Nº 3")]                           // Spanish
    [InlineData("São João, Conceição, Gonçalves, IRMÃ, SÃO PAULO, Ângela")]                         // Portuguese
    [InlineData("Åsa Ødegård, Bjørn Ærøskøbing, ÅÄÖ, Ø°, Æsir")]                                    // Scandinavian
    [InlineData("Þórður Ðorsteinsdóttir, Guðrún Þórisdóttir")]                                       // Icelandic
    [InlineData("Łódź, Wrocław, Zażółć gęślą jaźń")]                                                 // Polish
    [InlineData("Antonín Dvořák, Šťastný, Žižkov, Ľubomír Ťažký")]                                  // Czech and Slovak
    [InlineData("Pál Erdős, Szőke Ödön, Ünnep")]                                                     // Hungarian
    [InlineData("İstanbul, Şükrü Gül, Çağrı Öztürk")]                                                // Turkish
    [InlineData("Ștefan Țepeș, Brașov, Râmnicu Vâlcea")]                                             // Romanian
    [InlineData("Nguyễn Thị Bích Ngọc, Trần Văn Đức")]                                              // Vietnamese
    [InlineData("Cal·la, Pere Martí i Pujol")]                                                       // Catalan
    [InlineData("Αθήνα, Γιώργος Παπαδόπουλος")]                                                     // Greek
    [InlineData("Иван Петрович Сидоров, Київ")]                                                      // Cyrillic
    [InlineData("Jose\u0301 Mu\u0303noz")]                                                           // combining marks
    [InlineData("25°C, ½ acre, © 1901, 3×4, 10÷2, £5, ¥100, § 12, ¶ 3, µg")]                         // Latin-1 symbols
    [InlineData("Married 4 May 1901 at Elmford … “quoted” — ‘aside’ – €5")]                          // typographic punctuation
    [InlineData("Plain ASCII text with no accents at all.")]
    [InlineData("")]
    public void RealTextInAnyLanguage_IsNotGarbled(string text) =>
        Assert.Null(GarbledText.Find(text));

    [Theory]
    [InlineData("Ã©", "é")]
    [InlineData("SchrÃ¶der", "ö")]
    [InlineData("MuÃ±oz", "ñ")]
    [InlineData("O\u00C3\u00A0Brien", "à")]
    [InlineData("doesnâ€™t", "’")]
    [InlineData("â€œquotedâ€\u009D", "“quoted”")]
    [InlineData("Â£5", "£")]
    [InlineData("WrocÅ‚aw", "ł")]
    [InlineData("Ð˜Ð²Ð°Ð½", "Иван")]
    [InlineData("ðŸ˜€", "😀")]
    [InlineData("Ã¢â‚¬Â¦", "…")]
    public void GarbledText_IsFoundWithItsRepair(string text, string repaired)
    {
        var found = GarbledText.Find(text);

        Assert.NotNull(found);
        Assert.Equal(repaired, found.Repaired);
    }

    [Fact]
    public void GarbledTextBesideCorrectText_IsFound_QuotingOnlyTheGarbledStretch()
    {
        var found = GarbledText.Find("Event Place Carter, Tennessee, United States Ã¢â‚¬Â¦ Walter Hart Marriage …");

        Assert.Equal(new GarbledTextRepair("Ã¢â‚¬Â¦", "…"), found);
    }

    [Fact]
    public void ALongGarbledStretch_IsShortenedInTheRepair()
    {
        var found = GarbledText.Find(string.Concat(Enumerable.Repeat("Ã©", 50)));

        Assert.Equal(63, found!.Garbled.Length);
        Assert.EndsWith("...", found.Garbled);
    }
}

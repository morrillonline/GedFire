namespace GedCore.Packages;

/// <summary>
/// One source, page and extract carries one QUAY in every item of a package, as it must within one draft; two items that
/// grade it differently are refused, naming both.
/// </summary>
internal static class OneGradePerExtractRule
{
    public static IEnumerable<string> Check(PackageContents package)
    {
        var reported = new HashSet<(PackageLocation, PackageLocation, string?, int?, int?)>();
        foreach (var quoted in package.Citations.Where(c => c.Quay is not null).GroupBy(c => (c.Page, c.DataText)))
        {
            var citations = quoted.ToList();
            for (int i = 0; i < citations.Count; i++)
                for (int j = i + 1; j < citations.Count; j++)
                {
                    var (a, b) = (citations[i], citations[j]);
                    if (a.Where == b.Where || a.Quay == b.Quay || !a.Source.SameAs(b.Source)) continue;
                    var (low, high) = a.Quay < b.Quay ? (a, b) : (b, a);
                    if (!reported.Add((low.Where, high.Where, a.Page, low.Quay, high.Quay))) continue;
                    yield return $"{low.Where} and {high.Where} cite {a.Source}, page \"{a.Page}\", with the same extract at " +
                                 $"QUAY {low.Quay} and QUAY {high.Quay}; one source, page and extract carries one QUAY in a package, " +
                                 "so grade it the same in both findings, or give the differently graded fact its own excerpt";
                }
        }
    }
}

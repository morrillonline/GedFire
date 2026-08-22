namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Jaro-Winkler string similarity (docs/design/mcp-server.md "Name similarity").
// Standard Jaro similarity plus the Winkler common-prefix bonus. Static, one
// public method, no dependencies — operates on whatever strings it is given;
// callers normalize first.
// ---------------------------------------------------------------------------

public static class JaroWinkler
{
    const double PrefixScale = 0.1;
    const int MaxPrefixLength = 4;

    /// <summary>
    /// Similarity of <paramref name="a"/> and <paramref name="b"/> in
    /// [0.0, 1.0]: standard Jaro similarity with the Winkler prefix bonus
    /// (scaling factor 0.1, common prefix capped at 4 characters). Symmetric.
    /// Returns 0.0 when either input is null or empty.
    /// </summary>
    public static double Similarity(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
        if (a == b) return 1.0;

        double jaro = JaroSimilarity(a, b);
        if (jaro <= 0.0) return 0.0;

        int prefix = CommonPrefixLength(a, b, MaxPrefixLength);
        return jaro + prefix * PrefixScale * (1.0 - jaro);
    }

    static double JaroSimilarity(string a, string b)
    {
        int aLen = a.Length, bLen = b.Length;
        int matchDistance = Math.Max(0, Math.Max(aLen, bLen) / 2 - 1);

        var aMatched = new bool[aLen];
        var bMatched = new bool[bLen];
        int matches = 0;

        for (int i = 0; i < aLen; i++)
        {
            int start = Math.Max(0, i - matchDistance);
            int end = Math.Min(i + matchDistance + 1, bLen);
            for (int j = start; j < end; j++)
            {
                if (bMatched[j] || a[i] != b[j]) continue;
                aMatched[i] = true;
                bMatched[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        int transpositions = 0;
        int k = 0;
        for (int i = 0; i < aLen; i++)
        {
            if (!aMatched[i]) continue;
            while (!bMatched[k]) k++;
            if (a[i] != b[k]) transpositions++;
            k++;
        }
        transpositions /= 2;

        double m = matches;
        return (m / aLen + m / bLen + (m - transpositions) / m) / 3.0;
    }

    static int CommonPrefixLength(string a, string b, int max)
    {
        int n = Math.Min(max, Math.Min(a.Length, b.Length));
        int i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }
}

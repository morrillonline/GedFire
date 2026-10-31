namespace GedCore.Apply;

/// <summary>Why a written date improves on the one the fact held, or <see cref="None"/> when it does not.</summary>
public enum DateImprovement
{
    /// <summary>The same date respelled, or a less specific one consistent with it.</summary>
    None,

    /// <summary>The fact held no date.</summary>
    First,

    /// <summary>More specific and consistent: 1877 to 14 APR 1877, ABT 1877 to 1877.</summary>
    Refined,

    /// <summary>A tighter bound inside the held one: BEF 1800 to BEF 1797.</summary>
    Narrowed,

    /// <summary>Replaces a date the new one contradicts: 17 OCT 1810 to BEF 27 SEP 1796.</summary>
    Corrected,
}

using GedCore.Drafting;

namespace GedCore.Policy;

/// <summary>One enforceable rule: reads the selected items and reports each violation as a sentence.</summary>
internal interface IPolicyRule
{
    /// <param name="document">What the document already holds on each record.</param>
    IEnumerable<string> Check(IReadOnlyList<PolicyItem> items, IRecordHistory document);
}

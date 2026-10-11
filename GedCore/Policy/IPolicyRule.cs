namespace GedCore.Policy;

/// <summary>One enforceable rule: reads the selected items and reports each violation as a sentence.</summary>
internal interface IPolicyRule
{
    IEnumerable<string> Check(IReadOnlyList<PolicyItem> items);
}

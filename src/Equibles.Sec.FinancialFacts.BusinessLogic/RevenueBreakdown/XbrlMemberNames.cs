namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// The one folding rule for comparing issuer XBRL extension member QNames: lowercase +
/// strip underscores. Issuers respell their own extension names across their own filings
/// (amd:DatacenterMember in every 10-K, amd:DataCenterMember in every 10-Q); comparing
/// ordinally splits one member into two half-series. The fold deliberately does NOT touch
/// digits (us-gaap:...Amount1 is a genuinely distinct element), plurals or typos, and it
/// keeps the namespace prefix, so two issuers' equal-looking local names never merge.
/// Standard-taxonomy names have exactly one spelling corpus-wide, so literal comparisons
/// against standard names (axis lists, alias tags) stay ordinal on purpose.
/// </summary>
public static class XbrlMemberNames
{
    public static string Fold(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        return name.Replace("_", "").ToLowerInvariant();
    }

    /// <summary>Equality under the fold, for GroupBy/ToDictionary over member strings.</summary>
    public static readonly IEqualityComparer<string> Comparer = new FoldEqualityComparer();

    private sealed class FoldEqualityComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            return string.Equals(Fold(x), Fold(y), StringComparison.Ordinal);
        }

        public int GetHashCode(string obj)
        {
            return (Fold(obj) ?? string.Empty).GetHashCode();
        }
    }
}

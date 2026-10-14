namespace Equibles.Sec.FinancialFacts.BusinessLogic.Splits;

/// <summary>How a stock split moves a financial fact's value.</summary>
public enum FactSplitKind
{
    /// <summary>
    /// A split leaves the value unchanged: money, ratios, and share quantities that are not
    /// the issuer's common-share count (preferred, authorized).
    /// </summary>
    None,

    /// <summary>A per-share amount, stated on the share basis of its filing date.</summary>
    PerShare,

    /// <summary>A common-share count in the financial statements, stated on the share basis of its filing date.</summary>
    StatedShareCount,

    /// <summary>The cover-page common-share count, a literal count on its as-of date.</summary>
    ObservedShareCount,
}

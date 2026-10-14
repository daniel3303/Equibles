namespace Equibles.Sec.FinancialFacts.BusinessLogic.Splits;

/// <summary>
/// A fact value on today's share basis. <see cref="Unresolved"/> keeps the filed value because the
/// splits after the fact's basis date cannot be attributed to the listing; callers disclose it.
/// </summary>
public readonly record struct FactSplitRestatement(
    decimal Value,
    FactSplitKind Kind,
    bool Adjusted,
    bool Unresolved
)
{
    /// <summary>Whether a split can move this fact at all.</summary>
    public bool IsSplitSensitive => Kind != FactSplitKind.None;
}

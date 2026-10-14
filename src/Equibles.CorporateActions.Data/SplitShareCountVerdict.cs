using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>The evidence a split row should carry; null evidence means the counts cannot tell.</summary>
public readonly record struct SplitShareCountVerdict(
    StockSplit Split,
    StockSplitShareCountEvidence? Evidence,
    DateOnly? AsOf
);

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// How a filer's equity book is spread across issuers. The weights are null when no position has
/// a value.
/// </summary>
public sealed record HolderConcentration
{
    public int StockCount { get; init; }
    public decimal? TopTenWeight { get; init; }
    public decimal? Herfindahl { get; init; }
    public decimal? EffectivePositions { get; init; }
}

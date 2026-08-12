namespace Equibles.Holdings.Repositories.Models;

public sealed class InstitutionPortfolioPosition
{
    public Guid CommonStockId { get; set; }

    public long Shares { get; set; }

    public long Value { get; set; }
}

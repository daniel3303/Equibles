namespace Equibles.Holdings.Repositories.Models;

public sealed class InstitutionPortfolioPosition
{
    public Guid CommonStockId { get; set; }

    // Null is the issuer's primary listing. A multi-series trust files every fund under one
    // issuer, so the issuer alone is not the security.
    public string ListedTicker { get; set; }

    public long Shares { get; set; }

    public long Value { get; set; }
}

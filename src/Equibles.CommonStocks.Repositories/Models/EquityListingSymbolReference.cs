namespace Equibles.CommonStocks.Repositories.Models;

public class EquityListingSymbolReference
{
    public Guid EquityIssuerId { get; set; }
    public Guid EquityListingId { get; set; }
    public string Ticker { get; set; }
}

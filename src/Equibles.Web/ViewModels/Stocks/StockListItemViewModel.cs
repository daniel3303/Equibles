namespace Equibles.Web.ViewModels.Stocks;

public class StockListItemViewModel {
    public string Ticker { get; set; }
    public string Name { get; set; }
    public string Industry { get; set; }
    public double MarketCapitalization { get; set; }
    public string Cusip { get; set; }
    public bool HasHoldings { get; set; }
    public bool HasShortVolume { get; set; }
    public bool HasShortInterest { get; set; }
    public bool HasFtd { get; set; }
}

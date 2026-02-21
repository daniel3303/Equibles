using Equibles.Congress.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class CongressionalTradesTabViewModel {
    public List<CongressionalTrade> Trades { get; set; } = [];
    public string Ticker { get; set; }
}

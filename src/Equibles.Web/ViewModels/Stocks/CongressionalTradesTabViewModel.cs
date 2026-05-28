using Equibles.Congress.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class CongressionalTradesTabViewModel : StockTabViewModel
{
    public List<CongressionalTrade> Trades { get; set; } = [];
}

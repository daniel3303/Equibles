using Equibles.InsiderTrading.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class InsiderTradingTabViewModel : StockTabViewModel
{
    public List<InsiderTransaction> Transactions { get; set; } = [];
}

using Equibles.InsiderTrading.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class InsiderTradingTabViewModel {
    public List<InsiderTransaction> Transactions { get; set; } = [];
    public string Ticker { get; set; }
}

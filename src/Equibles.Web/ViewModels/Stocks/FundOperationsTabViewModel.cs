using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class FundOperationsTabViewModel : StockTabViewModel
{
    public List<NCenFiling> Filings { get; set; } = [];
}

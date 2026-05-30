using Equibles.InsiderTrading.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class ProposedSalesTabViewModel : StockTabViewModel
{
    public List<Form144Filing> Filings { get; set; } = [];
}

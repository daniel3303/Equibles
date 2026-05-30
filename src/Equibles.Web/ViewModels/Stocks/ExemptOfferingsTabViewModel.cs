using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class ExemptOfferingsTabViewModel : StockTabViewModel
{
    public List<FormDFiling> Filings { get; set; } = [];
}

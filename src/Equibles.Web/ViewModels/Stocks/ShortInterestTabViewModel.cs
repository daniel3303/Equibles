using Equibles.Finra.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class ShortInterestTabViewModel : StockTabViewModel
{
    public List<ShortInterest> ShortInterests { get; set; } = [];
}

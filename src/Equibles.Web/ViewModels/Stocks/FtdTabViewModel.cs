using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class FtdTabViewModel : StockTabViewModel
{
    public List<FailToDeliver> FailsToDeliver { get; set; } = [];
}

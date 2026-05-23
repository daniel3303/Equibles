using Equibles.CommonStocks.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class StockDetailViewModel
{
    public CommonStock Stock { get; set; }
    public string ActiveTab { get; set; }

    public int RecentFilingCount { get; set; }
    public int RecentFilerCount { get; set; }
    public int FilingActivityDays { get; set; }
}

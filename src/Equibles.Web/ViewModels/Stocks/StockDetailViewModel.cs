using Equibles.CommonStocks.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class StockDetailViewModel
{
    public CommonStock Stock { get; set; }
    public string ActiveTab { get; set; }

    public int RecentFilingCount { get; set; }
    public int RecentFilerCount { get; set; }
    public int FilingActivityDays { get; set; }

    // Gate the fund-only tabs: true only when the stock is a registered fund that
    // files NPORT (holdings) / N-CEN (operations). Operating companies file neither,
    // so the tabs are hidden rather than shown empty.
    public bool HasFundHoldings { get; set; }
    public bool HasFundOperations { get; set; }

    public KeyMetricsViewModel KeyMetrics { get; set; }
}

using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class FundHoldingsTabViewModel : StockTabViewModel
{
    /// <summary>The fund's most recent NPORT-P report, or null when none has been filed.</summary>
    public NportFiling Filing { get; set; }

    /// <summary>The largest holdings of <see cref="Filing"/> by value, newest report first.</summary>
    public List<NportHolding> Holdings { get; set; } = [];

    /// <summary>The total number of holdings on the report, before the display cap.</summary>
    public int TotalHoldings { get; set; }
}

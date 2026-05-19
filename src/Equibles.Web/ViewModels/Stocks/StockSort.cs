using System.ComponentModel.DataAnnotations;

namespace Equibles.Web.ViewModels.Stocks;

/// <summary>Sort options for the stock browser. Display names drive the sort dropdown.</summary>
public enum StockSort
{
    [Display(Name = "Ticker (A–Z)")]
    Ticker,

    [Display(Name = "Name (A–Z)")]
    Name,

    [Display(Name = "Market cap (high → low)")]
    MarketCapDescending,

    [Display(Name = "Market cap (low → high)")]
    MarketCapAscending,
}

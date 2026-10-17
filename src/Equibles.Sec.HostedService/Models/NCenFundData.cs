using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Models;

/// <summary>One FUND_REPORTED_INFO row of an N-CEN data set, with its advisers and tickers.</summary>
public class NCenFundData
{
    public string FundId { get; set; }
    public string SeriesId { get; set; }
    public string FundName { get; set; }
    public string Lei { get; set; }
    public NCenFundTypes FundTypes { get; set; }
    public bool? IsIndexAffiliated { get; set; }
    public bool? IsIndexExclusive { get; set; }
    public decimal? MonthlyAverageNetAssets { get; set; }
    public decimal? DailyAverageNetAssets { get; set; }
    public List<NCenAdviserData> Advisers { get; set; } = [];
    public List<NCenShareClassData> ShareClasses { get; set; } = [];
    public List<NCenExchangeListingData> ExchangeListings { get; set; } = [];
}

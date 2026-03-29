namespace Equibles.Integrations.Cftc.Models;

public class CftcReportRecord {
    public string MarketAndExchangeName { get; set; }
    public string ReportDate { get; set; }
    public string ContractMarketCode { get; set; }
    public long? OpenInterest { get; set; }
    public long? NonCommLong { get; set; }
    public long? NonCommShort { get; set; }
    public long? NonCommSpreads { get; set; }
    public long? CommLong { get; set; }
    public long? CommShort { get; set; }
    public long? TotalRptLong { get; set; }
    public long? TotalRptShort { get; set; }
    public long? NonRptLong { get; set; }
    public long? NonRptShort { get; set; }
    public long? ChangeOpenInterest { get; set; }
    public long? ChangeNonCommLong { get; set; }
    public long? ChangeNonCommShort { get; set; }
    public long? ChangeCommLong { get; set; }
    public long? ChangeCommShort { get; set; }
    public decimal? PctNonCommLong { get; set; }
    public decimal? PctNonCommShort { get; set; }
    public decimal? PctCommLong { get; set; }
    public decimal? PctCommShort { get; set; }
    public int? TradersTotal { get; set; }
    public int? TradersNonCommLong { get; set; }
    public int? TradersNonCommShort { get; set; }
    public int? TradersCommLong { get; set; }
    public int? TradersCommShort { get; set; }
}

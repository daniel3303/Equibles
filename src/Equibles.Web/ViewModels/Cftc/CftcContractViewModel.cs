using Equibles.Cftc.Data.Models;

namespace Equibles.Web.ViewModels.Cftc;

public class CftcContractViewModel {
    public string MarketCode { get; set; }
    public string MarketName { get; set; }
    public CftcContractCategory Category { get; set; }
    public string CategoryDisplayName { get; set; }
    public List<CftcReportItem> Reports { get; set; } = [];

    // Statistics
    public long? LatestOpenInterest { get; set; }
    public long? LatestCommercialNet { get; set; }
    public long? LatestNonCommercialNet { get; set; }
    public long? LatestNonCommSpreads { get; set; }
}

public class CftcReportItem {
    public DateOnly ReportDate { get; set; }
    public long OpenInterest { get; set; }
    public long CommLong { get; set; }
    public long CommShort { get; set; }
    public long NonCommLong { get; set; }
    public long NonCommShort { get; set; }
    public long NonCommSpreads { get; set; }
    public long? ChangeOpenInterest { get; set; }
}

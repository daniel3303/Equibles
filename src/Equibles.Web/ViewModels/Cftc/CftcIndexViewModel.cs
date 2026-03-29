using Equibles.Cftc.Data.Models;

namespace Equibles.Web.ViewModels.Cftc;

public class CftcIndexViewModel {
    public List<CftcCategoryGroup> Categories { get; set; } = [];
}

public class CftcCategoryGroup {
    public CftcContractCategory Category { get; set; }
    public string DisplayName { get; set; }
    public List<CftcContractItem> Contracts { get; set; } = [];
}

public class CftcContractItem {
    public string MarketCode { get; set; }
    public string MarketName { get; set; }
    public long? CommercialNet { get; set; }
    public long? NonCommercialNet { get; set; }
    public DateOnly? LatestDate { get; set; }
}

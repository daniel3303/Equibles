using Equibles.Errors.Data.Models;

namespace Equibles.Web.Models;

public class SystemStatusViewModel {
    public bool DatabaseConnected { get; set; }
    public bool McpApiKeyConfigured { get; set; }
    public List<WorkerStatus> Workers { get; set; } = [];
    public List<Error> RecentErrors { get; set; } = [];
    public int TotalErrorCount { get; set; }
    public int UnseenErrorCount { get; set; }

    // Data counts
    public int StockCount { get; set; }
    public int DocumentCount { get; set; }
    public int InsiderTransactionCount { get; set; }
    public int CongressionalTradeCount { get; set; }
    public int InstitutionalHoldingCount { get; set; }
    public int FailToDeliverCount { get; set; }
}

public class WorkerStatus {
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Active { get; set; }
    public string Reason { get; set; }
}

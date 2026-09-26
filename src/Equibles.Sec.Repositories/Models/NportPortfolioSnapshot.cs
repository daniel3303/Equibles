using Equibles.Sec.Data.Helpers;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories.Models;

public sealed class NportPortfolioSnapshot
{
    public Guid FilingId { get; set; }
    public DateOnly ReportPeriodDate { get; set; }
    public DateOnly FilingDate { get; set; }
    public decimal NetAssets { get; set; }
    public decimal TotalAssets { get; set; }
    public int TotalHoldings { get; set; }
    public int? ReportedHoldingCount { get; set; }
    public List<NportHolding> Holdings { get; set; } = [];

    public string HoldingsCoverage =>
        FundHoldingsCoverage.Classify(TotalHoldings, ReportedHoldingCount);
}

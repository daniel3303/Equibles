using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionProfileViewModel
{
    public string Name { get; set; }
    public string Cik { get; set; }
    public string Location { get; set; }
    public FundClassification Classification { get; set; }
    public bool ConfidentialTreatmentRequested { get; set; }
    public List<HoldingRowViewModel> Holdings { get; set; } = [];
    public InstitutionPortfolioSummary Summary { get; set; } = new();
    public List<IndustryAllocationSlice> IndustryAllocation { get; set; } = [];

    public List<DateOnly> AvailableReportDates { get; set; } = [];
    public DateOnly? ActivityDate { get; set; }
    public DateOnly? ActivityPriorDate { get; set; }
    public Dictionary<
        StockPositionChangeType,
        List<StockPositionChange>
    > QuarterlyActivity { get; set; } = [];

    public const int ActivityRowCap = 50;
}

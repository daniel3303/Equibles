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

    // Latest fund score for the default rolling window / benchmark, or null when this filer
    // hasn't been scored yet (e.g. no price history for its holdings).
    public FundScore FundScore { get; set; }

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

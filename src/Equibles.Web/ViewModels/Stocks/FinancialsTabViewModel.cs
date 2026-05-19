using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Web.ViewModels.Stocks;

public class FinancialsTabViewModel
{
    public string Ticker { get; set; }

    public FinancialStatementType StatementType { get; set; }

    public List<FinancialsPeriodOption> AvailablePeriods { get; set; } = [];

    public int SelectedYear { get; set; }

    public SecFiscalPeriod SelectedPeriod { get; set; }

    public List<FinancialsLineViewModel> Lines { get; set; } = [];

    /// <summary>True when the company has at least one ingested fact at all.</summary>
    public bool HasData => AvailablePeriods.Count > 0;
}

using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Web.ViewModels.Stocks;

/// <summary>
/// One selectable fiscal period (year + FY/Q1–Q4) for which the company has at
/// least one ingested fact. <see cref="Token"/> is the round-trippable query
/// value (<c>2023-FullYear</c>) used by the period <c>&lt;select&gt;</c>.
/// </summary>
public class FinancialsPeriodOption
{
    public FinancialsPeriodOption(int fiscalYear, SecFiscalPeriod fiscalPeriod, string label)
    {
        FiscalYear = fiscalYear;
        FiscalPeriod = fiscalPeriod;
        Label = label;
    }

    public int FiscalYear { get; }

    public SecFiscalPeriod FiscalPeriod { get; }

    public string Label { get; }

    public string Token => $"{FiscalYear}-{FiscalPeriod}";
}

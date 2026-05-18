namespace Equibles.CommonStocks.BusinessLogic;

/// <summary>
/// A company's fiscal year and quarter for a given date. <see cref="Year"/> is
/// labelled by the calendar year in which the fiscal year <em>ends</em> — the
/// US filer convention (Apple's October-2023 quarter is FY2024 Q1; a
/// calendar-year filer's 2024 is simply FY2024).
/// </summary>
public readonly record struct FiscalPeriod(int Year, int Quarter)
{
    public override string ToString() => $"FY{Year} Q{Quarter}";
}

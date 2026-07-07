namespace Equibles.Integrations.Sec.Models;

/// <summary>
/// One row of SEC's fund-class ticker directory (company_tickers_mf.json): a registered fund
/// share class with its trading symbol, keyed to the fund series it belongs to. This is the
/// authoritative ticker source for registered investment companies — their NPORT-P filings carry
/// series/class ids but no trading symbol.
/// </summary>
public class FundClassTicker
{
    public string Cik { get; set; }
    public string SeriesId { get; set; }
    public string ClassId { get; set; }
    public string Symbol { get; set; }
}

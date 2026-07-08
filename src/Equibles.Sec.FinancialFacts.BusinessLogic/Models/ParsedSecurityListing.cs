namespace Equibles.Sec.FinancialFacts.BusinessLogic.Models;

/// <summary>
/// One row of a filing's cover-page 12(b) registration table, as parsed from the
/// inline-XBRL envelope: the security's title with the trading symbol and
/// exchange filed in the same XBRL context. Symbol and exchange are null when
/// the filer omitted them from the title's context.
/// </summary>
public class ParsedSecurityListing
{
    public string Title { get; set; }
    public string TradingSymbol { get; set; }
    public string ExchangeName { get; set; }
}

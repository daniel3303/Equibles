namespace Equibles.Integrations.Lse.Models;

// One line of the exchange's own instrument list, as the workbook states it.
public sealed class LseInstrument
{
    public string Tidm { get; set; }
    public string IssuerName { get; set; }
    public string InstrumentName { get; set; }
    public string Isin { get; set; }
    public string MifirIdentifier { get; set; }
    public string TradingCurrency { get; set; }
    public string LseMarket { get; set; }

    // The market identifier code the stated LSE market maps to; null when the workbook names a market this
    // build does not know, which skips the line rather than guessing a venue.
    public string MarketIdentifierCode { get; set; }
}

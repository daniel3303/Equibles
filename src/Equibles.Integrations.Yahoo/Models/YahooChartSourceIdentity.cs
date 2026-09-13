namespace Equibles.Integrations.Yahoo.Models;

// Verbatim provider identity; currency codes may name minor units (for example GBp).
// The provider's exchange code is not an ISO MIC and must not be stored as one.
public class YahooChartSourceIdentity
{
    public string Symbol { get; set; }
    public string Currency { get; set; }
    public string ExchangeCode { get; set; }
    public string ExchangeName { get; set; }
    public string InstrumentType { get; set; }
    public string ExchangeTimeZone { get; set; }
}

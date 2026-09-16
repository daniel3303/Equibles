namespace Equibles.Integrations.Gpw.Models;

// One row of a main-market quotations table; the shortcut is the venue's ticker.
public sealed class GpwQuotation
{
    public string Isin { get; set; }
    public string Name { get; set; }
    public string Shortcut { get; set; }
    public string Currency { get; set; }
    public string MarketIdentifierCode { get; set; }
}

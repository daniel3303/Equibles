namespace Equibles.Integrations.NasdaqNordic.Models;

// One row of the shares screener; the symbol is the venue's own spelling, share class after a space.
public sealed class NasdaqNordicShare
{
    public string FullName { get; set; }
    public string Symbol { get; set; }
    public string Isin { get; set; }
    public string Currency { get; set; }
    public string OrderbookId { get; set; }
    public string AssetClass { get; set; }
    public string Sector { get; set; }
}

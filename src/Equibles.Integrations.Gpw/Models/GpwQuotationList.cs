namespace Equibles.Integrations.Gpw.Models;

// The main market publishes one table per trading system; the directory is their union.
public sealed class GpwQuotationList
{
    public DateTime CapturedAt { get; set; }
    public List<GpwQuotationTable> Tables { get; set; } = [];

    public IEnumerable<GpwQuotation> Quotations => Tables.SelectMany(table => table.Quotations);
}

public sealed class GpwQuotationTable
{
    public string TradingSystem { get; set; }
    public Uri SourceUrl { get; set; }
    public List<GpwQuotation> Quotations { get; set; } = [];
    public string Html { get; set; }
}

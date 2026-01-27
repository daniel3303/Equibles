namespace Equibles.ShortData.HostedService.Models;

internal class FtdRecord {
    public DateOnly SettlementDate { get; set; }
    public string Cusip { get; set; }
    public string Symbol { get; set; }
    public long Quantity { get; set; }
    public decimal Price { get; set; }
}

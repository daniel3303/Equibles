namespace Equibles.Holdings.Repositories.Models;

public class BacktestPosition
{
    public Guid CommonStockId { get; set; }

    public long Shares { get; set; }

    public long Value { get; set; }

    public bool IsOption { get; set; }
}

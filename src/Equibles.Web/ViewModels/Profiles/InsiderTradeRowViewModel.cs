namespace Equibles.Web.ViewModels.Profiles;

public class InsiderTradeRowViewModel
{
    public string Ticker { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string SecurityTitle { get; set; }
    public long Shares { get; set; }
    public decimal PricePerShare { get; set; }
}

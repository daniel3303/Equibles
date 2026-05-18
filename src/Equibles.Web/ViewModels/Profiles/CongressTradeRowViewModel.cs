namespace Equibles.Web.ViewModels.Profiles;

public class CongressTradeRowViewModel
{
    public string Ticker { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string AssetName { get; set; }
    public string OwnerType { get; set; }
    public long AmountFrom { get; set; }
    public long AmountTo { get; set; }
}

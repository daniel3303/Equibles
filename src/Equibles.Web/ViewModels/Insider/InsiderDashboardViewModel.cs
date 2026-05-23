namespace Equibles.Web.ViewModels.Insider;

public class InsiderDashboardViewModel
{
    public const int RowCap = 25;

    public List<InsiderDashboardRow> TopBuys { get; set; } = [];
    public List<InsiderDashboardRow> TopSells { get; set; } = [];
    public List<InsiderDashboardRow> BiggestTransactions { get; set; } = [];
}

public class InsiderDashboardRow
{
    public string OwnerName { get; set; }
    public string OwnerCik { get; set; }
    public string Ticker { get; set; }
    public DateOnly TransactionDate { get; set; }
    public long Shares { get; set; }
    public decimal PricePerShare { get; set; }
    public string SecurityTitle { get; set; }
    public string TransactionCodeLabel { get; set; }
    public bool IsAcquisition { get; set; }

    public decimal Value => Shares * PricePerShare;
}

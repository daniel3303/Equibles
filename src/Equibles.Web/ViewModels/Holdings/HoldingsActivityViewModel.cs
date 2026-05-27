namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsActivityViewModel : QuarterlySelectionViewModel
{
    public List<HoldingsActivityRow> TopBuys { get; set; } = [];
    public List<HoldingsActivityRow> TopSells { get; set; } = [];
    public List<HoldingsActivityRow> NewPositions { get; set; } = [];
    public List<HoldingsActivityRow> SoldOutPositions { get; set; } = [];

    public const int RowCap = 20;
}

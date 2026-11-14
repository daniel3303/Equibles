using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class HolderDetailViewModel
{
    public EquityIssuer Stock { get; set; }
    public InstitutionalHolder Holder { get; set; }

    // Null until the holder is profiled.
    public HolderEntityType? HolderType { get; set; }
    public const int MaxRows = 50;

    // Newest first, capped at MaxRows.
    public List<InstitutionalHolding> Holdings { get; set; } = [];

    // Share change against the same line in the earlier quarter, by row; a row with no earlier
    // quarter has none.
    public Dictionary<Guid, double> ChangePercent { get; set; } = [];
}

using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class HolderDetailViewModel
{
    public EquityIssuer Stock { get; set; }
    public InstitutionalHolder Holder { get; set; }
    public List<InstitutionalHolding> Holdings { get; set; } = [];
}

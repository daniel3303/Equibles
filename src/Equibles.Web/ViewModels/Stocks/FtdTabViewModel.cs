using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class FtdTabViewModel {
    public List<FailToDeliver> FailsToDeliver { get; set; } = [];
    public string Ticker { get; set; }
}

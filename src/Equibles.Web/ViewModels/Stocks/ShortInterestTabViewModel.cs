using Equibles.ShortData.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class ShortInterestTabViewModel {
    public List<ShortInterest> ShortInterests { get; set; } = [];
    public string Ticker { get; set; }
}

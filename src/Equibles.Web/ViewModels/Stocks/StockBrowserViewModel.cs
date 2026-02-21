namespace Equibles.Web.ViewModels.Stocks;

public class StockBrowserViewModel {
    public List<StockListItemViewModel> Stocks { get; set; } = [];
    public string Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class DocumentsTabViewModel {
    public List<Document> Documents { get; set; } = [];
    public string Ticker { get; set; }
}

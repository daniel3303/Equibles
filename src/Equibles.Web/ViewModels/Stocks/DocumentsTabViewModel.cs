using Equibles.Sec.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class DocumentsTabViewModel : StockTabViewModel
{
    public List<Document> Documents { get; set; } = [];
}

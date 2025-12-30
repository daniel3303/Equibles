using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Configuration;

public class DocumentScraperOptions {
    public DateTime? MinScrapingDate { get; set; }
    public List<string> TickersToSync { get; set; } = [];
    public List<DocumentType> DocumentTypesToSync { get; set; } = [
        DocumentType.TenK,
        DocumentType.TenQ,
        DocumentType.EightK,
        DocumentType.FormFour,
        DocumentType.FormThree
    ];
}
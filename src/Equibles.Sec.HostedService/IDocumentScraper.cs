using Equibles.Sec.HostedService.Models;

namespace Equibles.Sec.HostedService;

public interface IDocumentScraper {
    Task<ScrapingResult> ScrapeDocuments(CancellationToken cancellationToken = default);
}
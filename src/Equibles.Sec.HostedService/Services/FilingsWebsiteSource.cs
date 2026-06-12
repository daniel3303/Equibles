using System.Text;
using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Sec.BusinessLogic.Websites;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Website source backed by the stocks' own stored SEC filings — the primary
/// source: Regulation S-K Item 101(e) makes the 10-K website disclosure
/// mandatory, DEF 14A carries the same disclosure, and a 10-Q occasionally
/// repeats it (the only filing available for companies that haven't reached
/// their first fiscal year-end). Reads documents already in the database, so it
/// costs no external traffic.
/// </summary>
public class FilingsWebsiteSource : IWebsiteSource
{
    // Filing types carrying the website disclosure, most reliable first.
    private static readonly DocumentType[] FilingTypes =
    [
        DocumentType.TenK,
        DocumentType.Def14A,
        DocumentType.TenQ,
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FilingsWebsiteSource> _logger;

    public FilingsWebsiteSource(
        IServiceScopeFactory scopeFactory,
        ILogger<FilingsWebsiteSource> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public int Priority => 10;

    public string Name => "SEC filings";

    public async Task<IReadOnlyDictionary<Guid, string>> FindWebsites(
        IReadOnlyList<WebsiteSourceStock> stocks,
        CancellationToken cancellationToken
    )
    {
        var results = new Dictionary<Guid, string>();
        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var website = await ExtractForStock(stock.Id, cancellationToken);
            if (website != null)
                results[stock.Id] = website;
        }

        return results;
    }

    private async Task<string> ExtractForStock(Guid stockId, CancellationToken cancellationToken)
    {
        foreach (var filingType in FilingTypes)
        {
            var text = await LoadLatestFilingText(stockId, filingType, cancellationToken);
            var website = FilingWebsiteExtractor.Extract(text);
            if (website != null)
            {
                _logger.LogDebug(
                    "Extracted website {Website} from latest {FilingType} of stock {StockId}",
                    website,
                    filingType,
                    stockId
                );
                return website;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads the normalised text of the stock's most recent filing of
    /// <paramref name="filingType"/>, or null when it has none. Content bytes are
    /// large, so each document gets its own short-lived scope instead of
    /// accumulating in one change tracker for the whole batch.
    /// </summary>
    private async Task<string> LoadLatestFilingText(
        Guid stockId,
        DocumentType filingType,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DocumentRepository>();

        var documentId = await repo.GetAll()
            .Where(d => d.CommonStockId == stockId && d.DocumentType == filingType)
            .OrderByDescending(d => d.ReportingDate)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (documentId == null)
            return null;

        var document = await repo.GetWithContent(documentId.Value);
        var bytes = document?.Content?.FileContent?.Bytes;
        return bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : null;
    }
}

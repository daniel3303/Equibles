using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Integrations.Yahoo.Contracts;
using Microsoft.Extensions.Logging;

namespace Equibles.Yahoo.HostedService.Services;

/// <summary>
/// Last-resort website source backed by the Yahoo Finance asset profile, keyed
/// by ticker. Lowest priority: it only sees the long tail the filings and
/// Wikidata sources left unfilled, keeping the dependency on Yahoo's unofficial
/// endpoint as small as possible. One profile request per stock, so a
/// per-ticker failure must not sink the rest of the batch.
/// </summary>
public class YahooWebsiteSource : IWebsiteSource
{
    private readonly IYahooFinanceClient _yahooClient;
    private readonly ILogger<YahooWebsiteSource> _logger;

    public YahooWebsiteSource(IYahooFinanceClient yahooClient, ILogger<YahooWebsiteSource> logger)
    {
        _yahooClient = yahooClient;
        _logger = logger;
    }

    public int Priority => 30;

    public string Name => "Yahoo asset profile";

    public async Task<IReadOnlyDictionary<Guid, string>> FindWebsites(
        IReadOnlyList<WebsiteSourceStock> stocks,
        CancellationToken cancellationToken
    )
    {
        var results = new Dictionary<Guid, string>();
        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(stock.Ticker))
                continue;

            try
            {
                var website = (await _yahooClient.GetCompanyProfile(stock.Ticker))?.Website;
                if (!string.IsNullOrWhiteSpace(website))
                    results[stock.Id] = website;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                // An unknown ticker or transient Yahoo hiccup is expected for the
                // long tail this source serves; skip the stock, keep the batch.
                _logger.LogDebug(
                    ex,
                    "Yahoo asset profile lookup failed for {Ticker}",
                    stock.Ticker
                );
            }
        }

        return results;
    }
}

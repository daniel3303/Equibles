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
    // Per-ticker ceiling for the Yahoo profile lookup. Yahoo's unofficial endpoint can hang a
    // response; without this cap one slow ticker stalls the whole sequential batch past its
    // commit step, so the batch persists nothing.
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(15);

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
                // Bound each lookup: Yahoo's unofficial endpoint occasionally hangs a
                // response, and with no per-call cap one slow ticker stalls the whole
                // sequential batch for minutes — long enough that the cycle never reaches
                // its commit step, so NO stock in the batch gets persisted. A tight timeout
                // abandons the slow ticker and keeps the batch moving.
                var website = (
                    await _yahooClient
                        .GetCompanyProfile(stock.Ticker)
                        .WaitAsync(LookupTimeout, cancellationToken)
                )?.Website;
                if (!string.IsNullOrWhiteSpace(website))
                    results[stock.Id] = website;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                // The lookup outran LookupTimeout — skip this ticker, keep the batch.
                _logger.LogDebug(
                    ex,
                    "Yahoo asset profile lookup timed out for {Ticker}",
                    stock.Ticker
                );
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

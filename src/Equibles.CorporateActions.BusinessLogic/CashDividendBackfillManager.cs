using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;

namespace Equibles.CorporateActions.BusinessLogic;

// One-time historical cash-dividend backfill for a single stock. The incremental
// Yahoo price sync only captures dividends going forward from the date it first
// ran (its chart window starts at the last stored price), so stocks that predate
// the dividend capture have no history. This manager issues ONE full-range chart
// request (since -> today, the same events=div|split fetch the price sync uses,
// sharing its client and therefore its rate limiter) and upserts the returned
// dividends through the existing idempotent capture path — safe to re-run.
[Service]
public class CashDividendBackfillManager
{
    private readonly IYahooFinanceClient _yahooClient;
    private readonly CashDividendCaptureManager _captureManager;

    public CashDividendBackfillManager(
        IYahooFinanceClient yahooClient,
        CashDividendCaptureManager captureManager
    )
    {
        _yahooClient = yahooClient;
        _captureManager = captureManager;
    }

    // Fetches the stock's dividend events from `since` through today and upserts
    // them into CashDividend. Returns the number of rows written (new ex-dates
    // plus restated amounts); a re-run over already-captured history returns 0.
    public async Task<int> BackfillHistory(
        CommonStock stock,
        DateOnly since,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var chartData = await _yahooClient.GetChart(stock.Ticker, since, today);

        cancellationToken.ThrowIfCancellationRequested();

        // Map Yahoo's dividend shape onto the source-neutral capture DTO at this
        // boundary (mirrors the price sync's CaptureDividends), so the capture
        // manager stays decoupled from the integration.
        var captured = chartData
            .Dividends.Select(d => new CapturedDividend
            {
                ExDate = d.Date,
                AmountPerShare = d.Amount,
                Source = CashDividendSource.Yahoo,
            })
            .ToList();

        return await _captureManager.Capture(stock, captured);
    }
}

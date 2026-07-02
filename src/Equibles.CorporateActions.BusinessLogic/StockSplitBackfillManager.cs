using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;

namespace Equibles.CorporateActions.BusinessLogic;

// One-time historical stock-split backfill for a single stock. The incremental
// Yahoo price sync only captures split events inside its forward-only chart
// window (from the last stored price), so a split that predates a stock's first
// sync is never recorded — leaving every split-basis restatement (share counts,
// per-share figures, historical valuation ratios) with a silent factor-1 no-op.
// This manager issues ONE full-range chart request (since -> today, the same
// events=div|split fetch the price sync uses, sharing its client and therefore
// its rate limiter) and upserts the returned splits through the existing
// idempotent capture path — safe to re-run. Newly captured splits carry a null
// PriceAdjustmentAppliedTime, so the price sync's split reconciliation re-pulls
// those stocks' fully-adjusted history on its next cycles without further work.
[Service]
public class StockSplitBackfillManager
{
    private readonly IYahooFinanceClient _yahooClient;
    private readonly StockSplitCaptureManager _captureManager;

    public StockSplitBackfillManager(
        IYahooFinanceClient yahooClient,
        StockSplitCaptureManager captureManager
    )
    {
        _yahooClient = yahooClient;
        _captureManager = captureManager;
    }

    // Fetches the stock's split events from `since` through today and upserts
    // them into StockSplit. Returns the number of rows written (new effective
    // dates plus changed ratios); a re-run over already-captured history
    // returns 0.
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

        // Map Yahoo's split shape onto the source-neutral capture DTO at this
        // boundary (mirrors the price sync's CaptureSplits), so the capture
        // manager stays decoupled from the integration.
        var captured = chartData
            .Splits.Select(s => new CapturedSplit
            {
                EffectiveDate = s.Date,
                Numerator = s.Numerator,
                Denominator = s.Denominator,
                Source = StockSplitSource.Yahoo,
            })
            .ToList();

        return await _captureManager.Capture(stock, captured);
    }
}

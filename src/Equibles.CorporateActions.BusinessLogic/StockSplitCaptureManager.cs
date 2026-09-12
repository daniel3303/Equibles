using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Upserts captured split events into StockSplit. The manager locks and revalidates
// the exact current listing before writing, so a company-sync reorder cannot attach one
// security's action to another. Idempotent by (listing, EffectiveDate): a
// re-run with the same events writes nothing. A changed ratio for the same exact
// source series clears PriceAdjustmentAppliedTime for another reconciliation.
[Service]
public class StockSplitCaptureManager
{
    private readonly StockSplitRepository _splitRepository;
    private readonly EquityIssuerRepository _stockRepository;

    public StockSplitCaptureManager(
        StockSplitRepository splitRepository,
        EquityIssuerRepository stockRepository
    )
    {
        _splitRepository = splitRepository;
        _stockRepository = stockRepository;
    }

    public Task<int> Capture(
        Guid commonStockId,
        string listedTicker,
        IReadOnlyCollection<CapturedSplit> splits,
        CancellationToken cancellationToken = default
    ) => CaptureListing(commonStockId, null, listedTicker, splits, cancellationToken);

    public Task<int> CaptureForListing(
        Guid equityIssuerId,
        Guid equityListingId,
        string sourceTicker,
        IReadOnlyCollection<CapturedSplit> splits,
        CancellationToken cancellationToken = default
    ) => CaptureListing(equityIssuerId, equityListingId, sourceTicker, splits, cancellationToken);

    public Task<int> CaptureForHistoricalListing(
        Guid equityIssuerId,
        Guid equityListingId,
        string sourceTicker,
        DateOnly expectedDelistedOn,
        IReadOnlyCollection<CapturedSplit> splits,
        CancellationToken cancellationToken = default
    ) =>
        CaptureListing(
            equityIssuerId,
            equityListingId,
            sourceTicker,
            splits,
            cancellationToken,
            expectedDelistedOn
        );

    private async Task<int> CaptureListing(
        Guid commonStockId,
        Guid? listingId,
        string listedTicker,
        IReadOnlyCollection<CapturedSplit> splits,
        CancellationToken cancellationToken,
        DateOnly? expectedDelistedOn = null
    )
    {
        if (splits == null || splits.Count == 0)
            return 0;

        await using var transaction = await _splitRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        await _stockRepository.BeginDirectoryIdentityWrite(cancellationToken);
        EquityIssuer stock = await _stockRepository.GetForUpdate(commonStockId, cancellationToken);
        var requestedTicker = TickerNormalizer.NormalizeListed(listedTicker);
        var candidates =
            stock
                ?.Securities.SelectMany(security => security.Listings)
                .Where(listing =>
                    (
                        expectedDelistedOn.HasValue
                            ? !listing.Active && listing.DelistedOn == expectedDelistedOn
                            : listing.Active
                    )
                    && listing.Ticker == requestedTicker
                    && (
                        listingId.HasValue
                            ? listing.Id == listingId.Value
                            : listing.MarketCountryCode == "US"
                    )
                )
                .Take(2)
                .ToList()
            ?? [];
        var listing = candidates.Count == 1 ? candidates[0] : null;
        if (listing == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var existing = await _splitRepository.GetByStock(stock.Id).ToListAsync(cancellationToken);
        var canAttributeRecordedSymbol =
            listing.MarketCountryCode == "US"
            && await _stockRepository.GetRecordedEquityListingId(stock.Id, listing.Ticker)
                == listing.Id;
        var changes = 0;

        foreach (var split in splits)
        {
            if (
                split.Numerator <= 0
                || split.Denominator <= 0
                || expectedDelistedOn.HasValue && split.EffectiveDate > expectedDelistedOn.Value
            )
                continue;

            var match = existing.SingleOrDefault(action =>
                action.EquityListingId == listing.Id && action.EffectiveDate == split.EffectiveDate
            );
            // Only old, explicitly recorded U.S. source symbols can establish missing attribution.
            match ??= canAttributeRecordedSymbol
                ? existing.SingleOrDefault(action =>
                    action.EquityListingId == null
                    && action.EffectiveDate == split.EffectiveDate
                    && action.PriceSeriesTicker == listing.Ticker
                )
                : null;
            if (match == null)
            {
                match = new StockSplit
                {
                    EquityIssuerId = stock.Id,
                    EquityListingId = listing.Id,
                    PriceSeriesTicker = listing.Ticker,
                    EffectiveDate = split.EffectiveDate,
                    Numerator = split.Numerator,
                    Denominator = split.Denominator,
                    Source = split.Source,
                };
                _splitRepository.Add(match);
                existing.Add(match);
                changes++;
            }
            else if (
                split.Source >= match.Source
                && (
                    match.EquityListingId == null
                    || match.Numerator != split.Numerator
                    || match.Denominator != split.Denominator
                    || match.Source != split.Source
                )
            )
            {
                var definitionChanged =
                    match.Numerator != split.Numerator
                    || match.Denominator != split.Denominator
                    || match.Source != split.Source;
                match.EquityListingId = listing.Id;
                match.Numerator = split.Numerator;
                match.Denominator = split.Denominator;
                match.Source = split.Source;
                // Attaching exact source identity alone does not change an already-applied ratio.
                if (definitionChanged)
                    match.PriceAdjustmentAppliedTime = null;
                changes++;
            }
        }

        if (changes > 0)
            await _splitRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }
}

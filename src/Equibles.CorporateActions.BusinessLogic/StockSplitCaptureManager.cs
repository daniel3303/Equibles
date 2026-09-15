using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.CommonStocks.Repositories.Models;
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
    // The same ratio on the same listing within this many days is one event reported twice:
    // Yahoo serves an announced and an executed date, the reference feed re-publishes a moved
    // execution date, and the two disagree on the day. A real repeat of one ratio (a monthly
    // stock dividend) sits a month apart. The collapse migration carries the same literal.
    public const int SameEventWindowDays = 10;

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
        CancellationToken cancellationToken = default,
        EquityListingSourceBinding expectedSourceBinding = null
    ) =>
        CaptureListing(
            equityIssuerId,
            equityListingId,
            sourceTicker,
            splits,
            cancellationToken,
            expectedSourceBinding: expectedSourceBinding
        );

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
        DateOnly? expectedDelistedOn = null,
        EquityListingSourceBinding expectedSourceBinding = null
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

        if (
            expectedSourceBinding != null
            && (
                expectedSourceBinding.EquityListingId != listing.Id
                || expectedSourceBinding.EquityIssuerId != stock.Id
                || !await _stockRepository
                    .GetSecurities()
                    .SelectMany(security => security.Listings)
                    .ForVerifiedSource(expectedSourceBinding)
                    .AnyAsync(cancellationToken)
            )
        )
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

            // Only old, explicitly recorded U.S. source symbols can establish missing attribution.
            // Attribution runs before the same-event collapse, so a legacy row is attached first
            // and merged on the next observation instead of deferring the issuer for ever.
            var legacy = canAttributeRecordedSymbol
                ? existing.SingleOrDefault(action =>
                    action.EquityListingId == null
                    && action.EffectiveDate == split.EffectiveDate
                    && action.PriceSeriesTicker == listing.Ticker
                )
                : null;
            if (legacy == null && CollapseSameEvent(existing, listing, split, ref changes))
                continue;

            var match =
                existing.SingleOrDefault(action =>
                    action.EquityListingId == listing.Id
                    && action.EffectiveDate == split.EffectiveDate
                ) ?? legacy;
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

    // One stored row per event: the highest-precedence source owns the date, and among equal
    // sources the later date wins because the earlier one is the announcement (Jan 1 is never
    // a trading day, the split moved to Jan 2). That order is a fixed point, so re-serving the
    // same two events writes nothing and never flaps the price-adjustment marker.
    private bool CollapseSameEvent(
        List<StockSplit> existing,
        EquityListing listing,
        CapturedSplit split,
        ref int changes
    )
    {
        var sameEvent = existing
            .Where(action =>
                action.EquityListingId == listing.Id
                && action.Numerator == split.Numerator
                && action.Denominator == split.Denominator
                && Math.Abs(action.EffectiveDate.DayNumber - split.EffectiveDate.DayNumber)
                    <= SameEventWindowDays
            )
            .OrderByDescending(action => action.Source)
            .ThenByDescending(action => action.EffectiveDate)
            .ThenByDescending(action => action.Id)
            .ToList();
        if (sameEvent.Count == 0)
            return false;
        // The incoming date may already be held by a different ratio on this listing; moving the
        // survivor there would violate the (listing, date) unique index. The exact-date path owns
        // that row and the next observation collapses what is left.
        if (
            existing.Any(action =>
                action.EquityListingId == listing.Id
                && action.EffectiveDate == split.EffectiveDate
                && !sameEvent.Contains(action)
            )
        )
            return false;

        var survivor = sameEvent[0];
        foreach (var duplicate in sameEvent.Skip(1))
        {
            _splitRepository.Delete(duplicate);
            existing.Remove(duplicate);
            changes++;
        }

        var outranks = split.Source > survivor.Source;
        var laterFromSameSource =
            split.Source == survivor.Source && split.EffectiveDate > survivor.EffectiveDate;
        if (outranks || laterFromSameSource)
        {
            survivor.EffectiveDate = split.EffectiveDate;
            survivor.Source = split.Source;
            // Mirrors the database trigger: a revised definition needs another reconciliation.
            survivor.PriceAdjustmentAppliedTime = null;
            changes++;
        }

        return true;
    }
}

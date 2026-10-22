using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.CommonStocks.Repositories.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Upserts captured split events into StockSplit. The manager locks and revalidates
// the exact current listing before writing, so a company-sync reorder cannot attach one
// security's action to another. Idempotent by (listing, EffectiveDate): a
// re-run with the same events writes nothing. A changed ratio for the same exact
// source series clears PriceAdjustmentAppliedTime for another reconciliation. A caller holding a
// source's complete report also lets that source retire its own rows on dates it moved away from,
// including a definition whose ratio it revised.
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

    /// <param name="reportedFrom">
    /// Set when <paramref name="splits"/> is the source's complete current report for this listing
    /// for effective dates on or after it; the source's own rows on dates the report has moved away
    /// from are then superseded before the report is captured.
    /// </param>
    public Task<int> CaptureForListing(
        Guid equityIssuerId,
        Guid equityListingId,
        string sourceTicker,
        IReadOnlyCollection<CapturedSplit> splits,
        CancellationToken cancellationToken = default,
        EquityListingSourceBinding expectedSourceBinding = null,
        DateOnly? reportedFrom = null
    ) =>
        CaptureListing(
            equityIssuerId,
            equityListingId,
            sourceTicker,
            splits,
            cancellationToken,
            expectedSourceBinding: expectedSourceBinding,
            reportedFrom: reportedFrom
        );

    /// <summary>
    /// Supersedes the source's own rows on dates its complete current report for this listing
    /// (effective dates on or after <paramref name="reportedFrom"/>) has moved away from, without
    /// capturing any reported event the listing does not already hold.
    /// </summary>
    public Task<int> SupersedeMovedDates(
        Guid equityIssuerId,
        Guid equityListingId,
        string sourceTicker,
        DateOnly reportedFrom,
        IReadOnlyCollection<CapturedSplit> report,
        CancellationToken cancellationToken = default
    ) =>
        CaptureListing(
            equityIssuerId,
            equityListingId,
            sourceTicker,
            report,
            cancellationToken,
            reportedFrom: reportedFrom,
            captureReported: false
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
        EquityListingSourceBinding expectedSourceBinding = null,
        DateOnly? reportedFrom = null,
        bool captureReported = true
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

        // A retired symbol another issuer holds was served as that holder's series, so its
        // splits are the holder's. Checked under the directory lock a new claim is written under.
        if (
            expectedDelistedOn.HasValue
            && await _stockRepository
                .GetForeignSymbolClaims(commonStockId, listing.Ticker, expectedDelistedOn.Value)
                .AnyAsync(cancellationToken)
        )
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
        if (reportedFrom.HasValue)
            SupersedeMovedDates(existing, listing, splits, reportedFrom.Value, ref changes);

        foreach (var split in captureReported ? splits : Array.Empty<CapturedSplit>())
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
            if (legacy == null && IsRevisedByHigherSource(existing, listing, split))
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
                    match.InvalidateRevisedDefinition();
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
                    <= SplitEvents.SameEventWindowDays
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
            survivor.InvalidateRevisedDefinition();
            changes++;
        }

        return true;
    }

    // A lower-precedence source can keep serving a definition the higher source has revised:
    // Yahoo's chart still lists MGN's withdrawn 1:40 nine days before the feed's 1:30. Inside the
    // same-event window a different ratio moving the share count the same way is that event's
    // stale definition, so it is not captured as a second split. Rows already stored are left to
    // their own source.
    private static bool IsRevisedByHigherSource(
        List<StockSplit> existing,
        EquityListing listing,
        CapturedSplit split
    ) =>
        existing.Any(action =>
            action.EquityListingId == listing.Id
            && action.Source > split.Source
            && Math.Abs(action.EffectiveDate.DayNumber - split.EffectiveDate.DayNumber)
                <= SplitEvents.SameEventWindowDays
            && SplitEvents.IsRevisedRatio(
                split.Numerator,
                split.Denominator,
                action.Numerator,
                action.Denominator
            )
        );

    // A source that re-dates a split (a record date corrected to the ex-date, a postponed reverse
    // split) or revises its ratio re-reports it and stops listing the old definition, whose date
    // can sit further away than SameEventWindowDays. Its own row on a date its complete report no
    // longer lists is that withdrawn definition when the report's nearest event had not passed
    // when the row was captured; an event already history by then was a different one, and a
    // genuinely separate split stays in the report. A same-ratio row merges into one already
    // holding the reported date, whose applied marker stands, or moves there and reopens
    // reconciliation like any revised definition. A revised ratio must still move the share count
    // the same way; it moves the row onto the reported definition, or merges into a holder already
    // carrying it and reopens that holder, since the stored closes were reconciled while the
    // withdrawn ratio stood beside it. A holder with any other ratio leaves the row alone.
    private void SupersedeMovedDates(
        List<StockSplit> existing,
        EquityListing listing,
        IReadOnlyCollection<CapturedSplit> report,
        DateOnly reportedFrom,
        ref int changes
    )
    {
        var reported = report.Where(split => split.Numerator > 0 && split.Denominator > 0).ToList();
        var sources = reported.Select(split => split.Source).Distinct().ToList();
        if (sources.Count != 1)
            return;
        var reportedDates = reported.Select(split => split.EffectiveDate).ToHashSet();
        var withdrawn = existing
            .Where(action =>
                action.EquityListingId == listing.Id
                && action.Source == sources[0]
                && action.PriceSeriesTicker == listing.Ticker
                && action.EffectiveDate >= reportedFrom
                && !reportedDates.Contains(action.EffectiveDate)
            )
            .OrderBy(action => action.EffectiveDate)
            .ThenBy(action => action.Id)
            .ToList();
        foreach (var stale in withdrawn)
        {
            // A row reported after its date had long passed was history when captured, not an
            // event the source could still re-date.
            var capturedOn = DateOnly.FromDateTime(stale.CreationTime);
            if (stale.EffectiveDate.AddDays(SplitEvents.SameEventWindowDays) < capturedOn)
                continue;
            // The nearest reported event is the only candidate; filtering before choosing would
            // let an unrelated later event stand in for a revision the guard refused.
            var replacement = reported
                .OrderBy(split =>
                    Math.Abs(split.EffectiveDate.DayNumber - stale.EffectiveDate.DayNumber)
                )
                .ThenByDescending(split => split.EffectiveDate)
                .FirstOrDefault();
            if (replacement == null || replacement.EffectiveDate < capturedOn)
                continue;
            var sameRatio =
                replacement.Numerator == stale.Numerator
                && replacement.Denominator == stale.Denominator;
            if (
                !sameRatio
                && !SplitEvents.IsRevisedRatio(
                    stale.Numerator,
                    stale.Denominator,
                    replacement.Numerator,
                    replacement.Denominator
                )
            )
                continue;

            var holder = existing.FirstOrDefault(action =>
                action.EquityListingId == listing.Id
                && action.EffectiveDate == replacement.EffectiveDate
            );
            if (holder == null)
            {
                stale.EffectiveDate = replacement.EffectiveDate;
                stale.Numerator = replacement.Numerator;
                stale.Denominator = replacement.Denominator;
                stale.InvalidateRevisedDefinition();
                changes++;
            }
            else if (
                holder.Numerator == replacement.Numerator
                && holder.Denominator == replacement.Denominator
            )
            {
                _splitRepository.Delete(stale);
                existing.Remove(stale);
                // Only the marker reopens: the holder's definition is unchanged, so the revision
                // trigger and its share-count verdict stay as they are.
                if (!sameRatio)
                    holder.PriceAdjustmentAppliedTime = null;
                changes++;
            }
        }
    }
}

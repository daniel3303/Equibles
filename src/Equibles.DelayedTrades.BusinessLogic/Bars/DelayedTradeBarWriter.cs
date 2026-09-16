using System.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.DelayedTrades.BusinessLogic.Sessions;
using Equibles.DelayedTrades.Repositories;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.DelayedTrades.BusinessLogic.Bars;

// One short transaction per bar: lock the issuer row, revalidate the listing, then insert or overwrite the
// session's row in the shared price store. Prices stay in the listing's quotation unit, as the Yahoo lane stores them.
[Service]
public class DelayedTradeBarWriter(
    IServiceScopeFactory scopeFactory,
    ILogger<DelayedTradeBarWriter> logger
)
{
    public async Task<DelayedTradeBarOutcome> Write(
        DelayedTradeListingReference target,
        DelayedTradeSessionBar bar,
        string currencyToken,
        DateOnly localToday,
        CancellationToken cancellationToken
    )
    {
        if (
            !bar.HasBar
            || !DailyBarGuards.IsValidCandle(
                bar.Open.Value,
                bar.High.Value,
                bar.Low.Value,
                bar.Close.Value,
                bar.Volume
            )
        )
            return DelayedTradeBarOutcome.SkippedInvalid;
        if (!DailyBarGuards.IsSettledDailyBar(bar.SessionDate, localToday))
            return DelayedTradeBarOutcome.Unsettled;

        using var scope = scopeFactory.CreateScope();
        var issuers = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var prices = scope.ServiceProvider.GetRequiredService<EquityDailyStockPriceRepository>();
        await using var transaction = await prices.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var issuer = await issuers.GetForUpdate(target.EquityIssuerId, cancellationToken);
        var listing = Revalidate(issuer, target, bar, currencyToken);
        if (listing == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return DelayedTradeBarOutcome.SkippedIdentity;
        }

        var stored = await prices
            .GetByListing(listing.Id)
            .SingleOrDefaultAsync(price => price.Date == bar.SessionDate, cancellationToken);
        var sourceKey = VenuePriceSource.Key(listing.MarketIdentifierCode, target.Isin);
        DelayedTradeBarOutcome outcome;
        if (stored == null)
        {
            prices.Add(
                new EquityDailyStockPrice
                {
                    EquityListingId = listing.Id,
                    SourceTicker = sourceKey,
                    Date = bar.SessionDate,
                    Open = bar.Open.Value,
                    High = bar.High.Value,
                    Low = bar.Low.Value,
                    Close = bar.Close.Value,
                    AdjustedClose = bar.Close.Value,
                    Volume = bar.Volume,
                }
            );
            outcome = DelayedTradeBarOutcome.Inserted;
        }
        else if (VenuePriceSource.IsVenueOwned(stored))
        {
            if (IsSameBar(stored, bar))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DelayedTradeBarOutcome.Unchanged;
            }
            Overwrite(stored, bar, sourceKey);
            outcome = DelayedTradeBarOutcome.Rederived;
        }
        else
        {
            // A Yahoo bar on another split basis belongs to the reconcile; the venue never restates it.
            if (!DailyBarGuards.IsSameSplitBasis(stored.Close, bar.Close.Value))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DelayedTradeBarOutcome.SkippedBasis;
            }
            logger.LogInformation(
                "Venue bar replaces the feed bar for {Listing} on {Date}: venue volume {VenueVolume}, feed volume {FeedVolume}",
                sourceKey,
                bar.SessionDate,
                bar.Volume,
                stored.Volume
            );
            Overwrite(stored, bar, sourceKey);
            outcome = DelayedTradeBarOutcome.OverwroteYahoo;
        }

        try
        {
            await prices.SaveChanges();
        }
        catch (DbUpdateException exception) when (outcome == DelayedTradeBarOutcome.Inserted)
        {
            // The feed inserted the same session between our read and our write; the next re-derivation overwrites it.
            logger.LogWarning(
                exception,
                "Venue bar for {Listing} on {Date} lost an insert race; left to the next re-derivation",
                sourceKey,
                bar.SessionDate
            );
            await transaction.RollbackAsync(cancellationToken);
            return DelayedTradeBarOutcome.Unchanged;
        }
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    // The locked graph must still carry exactly the listing the print was matched to.
    internal static EquityListing Revalidate(
        EquityIssuer issuer,
        DelayedTradeListingReference target,
        DelayedTradeSessionBar bar,
        string currencyToken
    )
    {
        if (issuer == null)
            return null;
        var claims = issuer
            .Securities.SelectMany(security =>
                security.Listings.Select(listing => (Security: security, Listing: listing))
            )
            .Where(pair =>
                pair.Listing.Active
                && pair.Listing.MarketIdentifierCode == bar.Venue
                && pair.Security.Isin == bar.Isin
            )
            .ToList();
        if (claims.Count != 1)
            return null;
        var listing = claims[0].Listing;
        if (
            listing.Id != target.EquityListingId
            || listing.IdentityState != EquityIdentityState.Verified
            || !EquityQuotationUnits.Matches(
                currencyToken,
                listing.TradingCurrency,
                listing.QuoteUnitMultiplier
            )
        )
            return null;
        return listing;
    }

    private static bool IsSameBar(EquityDailyStockPrice stored, DelayedTradeSessionBar bar) =>
        stored.Open == bar.Open
        && stored.High == bar.High
        && stored.Low == bar.Low
        && stored.Close == bar.Close
        && stored.Volume == bar.Volume;

    // The adjusted close follows the close only while the row was unadjusted; a rebased figure is the feed's to keep.
    private static void Overwrite(
        EquityDailyStockPrice stored,
        DelayedTradeSessionBar bar,
        string sourceKey
    )
    {
        var unadjusted = stored.AdjustedClose == stored.Close;
        stored.Open = bar.Open.Value;
        stored.High = bar.High.Value;
        stored.Low = bar.Low.Value;
        stored.Close = bar.Close.Value;
        if (unadjusted)
            stored.AdjustedClose = bar.Close.Value;
        stored.Volume = bar.Volume;
        stored.SourceTicker = sourceKey;
    }
}

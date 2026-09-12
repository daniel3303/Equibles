using System.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Captures exact listing payments; earlier issuer-only observations remain independent evidence.
[Service]
public class CashDividendCaptureManager
{
    private readonly CashDividendRepository _dividendRepository;
    private readonly EquityIssuerRepository _stockRepository;

    public CashDividendCaptureManager(
        CashDividendRepository dividendRepository,
        EquityIssuerRepository stockRepository
    )
    {
        _dividendRepository = dividendRepository;
        _stockRepository = stockRepository;
    }

    public Task<int> Capture(
        Guid commonStockId,
        string listedTicker,
        IReadOnlyCollection<CapturedDividend> dividends,
        CancellationToken cancellationToken = default
    ) => CaptureListing(commonStockId, null, listedTicker, dividends, cancellationToken);

    public Task<int> CaptureForListing(
        Guid equityIssuerId,
        Guid equityListingId,
        string sourceTicker,
        IReadOnlyCollection<CapturedDividend> dividends,
        CancellationToken cancellationToken = default
    ) =>
        CaptureListing(equityIssuerId, equityListingId, sourceTicker, dividends, cancellationToken);

    public Task<int> CaptureForHistoricalListing(
        Guid equityIssuerId,
        Guid equityListingId,
        string sourceTicker,
        DateOnly expectedDelistedOn,
        IReadOnlyCollection<CapturedDividend> dividends,
        CancellationToken cancellationToken = default
    ) =>
        CaptureListing(
            equityIssuerId,
            equityListingId,
            sourceTicker,
            dividends,
            cancellationToken,
            expectedDelistedOn
        );

    private async Task<int> CaptureListing(
        Guid commonStockId,
        Guid? listingId,
        string listedTicker,
        IReadOnlyCollection<CapturedDividend> dividends,
        CancellationToken cancellationToken,
        DateOnly? expectedDelistedOn = null
    )
    {
        if (dividends == null || dividends.Count == 0)
            return 0;

        var combinedDividends = CombineSameDateDividends(dividends);
        if (combinedDividends.Count == 0)
            return 0;

        await using var transaction = await _dividendRepository.CreateTransaction(
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
        if (
            listing == null
            || listing.TradingCurrency == null
            || (!listingId.HasValue && listing.Id != stock.Presentation?.EquityListingId)
        )
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var existing = await _dividendRepository
            .GetByListing(listing.Id)
            .ToListAsync(cancellationToken);
        var changes = 0;

        foreach (var dividend in combinedDividends)
        {
            if (
                dividend.Currency != listing.TradingCurrency
                || expectedDelistedOn.HasValue && dividend.ExDate > expectedDelistedOn.Value
            )
                continue;
            var match = existing.SingleOrDefault(d => d.ExDate == dividend.ExDate);
            if (match == null)
            {
                _dividendRepository.Add(
                    new CashDividend
                    {
                        EquityIssuerId = stock.Id,
                        EquityListingId = listing.Id,
                        Currency = dividend.Currency,
                        ExDate = dividend.ExDate,
                        AmountPerShare = dividend.AmountPerShare,
                        Source = dividend.Source,
                    }
                );
                changes++;
            }
            else if (
                match.Currency == dividend.Currency
                && CanSupersede(match.Source, dividend.Source)
            )
            {
                var amountChanged = match.AmountPerShare != dividend.AmountPerShare;
                var sourceChanged = match.Source != dividend.Source;
                if (!amountChanged && !sourceChanged)
                    continue;

                if (amountChanged)
                {
                    match.AmountPerShare = dividend.AmountPerShare;
                    match.PriceAdjustmentAppliedAmountPerShare = null;
                    match.PriceAdjustmentAppliedTime = null;
                }

                match.Source = dividend.Source;
                changes++;
            }
        }

        if (changes > 0)
            await _dividendRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

    // Manual values are operator-owned. Yahoo supplies the adjusted price history, so its
    // dividend amount must remain stable against a later generic external-reference refresh.
    private static bool CanSupersede(
        CashDividendSource currentSource,
        CashDividendSource incomingSource
    ) =>
        incomingSource == currentSource
        || SourcePriority(incomingSource) > SourcePriority(currentSource);

    private static int SourcePriority(CashDividendSource source) =>
        source switch
        {
            CashDividendSource.External => 0,
            CashDividendSource.Yahoo => 1,
            CashDividendSource.Manual => 2,
            _ => int.MinValue,
        };

    internal static List<CapturedDividend> CombineSameDateDividends(
        IReadOnlyCollection<CapturedDividend> dividends
    ) =>
        dividends
            .GroupBy(dividend => dividend.ExDate)
            .Where(group =>
                group.All(dividend =>
                    dividend.AmountPerShare > 0
                    && dividend.Currency is { Length: 3 }
                    && dividend.Currency.All(character => character is >= 'A' and <= 'Z')
                )
                && group
                    .Select(dividend => dividend.Currency)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == 1
            )
            .Select(CombineSameDateDividend)
            .ToList();

    private static CapturedDividend CombineSameDateDividend(
        IGrouping<DateOnly, CapturedDividend> dividends
    )
    {
        var sources = dividends.Select(dividend => dividend.Source).Distinct().ToList();
        if (sources.Count != 1)
        {
            throw new InvalidOperationException(
                $"Cash dividends on {dividends.Key:yyyy-MM-dd} must come from one source per capture batch."
            );
        }

        return new CapturedDividend
        {
            ExDate = dividends.Key,
            AmountPerShare = dividends.Sum(dividend => dividend.AmountPerShare),
            Source = sources[0],
            Currency = dividends.First().Currency,
        };
    }
}

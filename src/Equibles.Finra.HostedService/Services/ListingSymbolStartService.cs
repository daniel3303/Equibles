using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Finra.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.HostedService.Services;

/// <summary>
/// Records, for each active US listing whose symbol another issuer stated on its SEC cover pages,
/// the date the symbol started naming the listing (<see cref="ListingSymbolStartRule"/>). The FINRA
/// imports map today's symbols onto every historical file, so without the start a recycled symbol
/// files its earlier holder's history under the listing that holds it now. A start that holds
/// across two refreshes withdraws the daily short-volume, short-interest and weekly off-exchange rows
/// already filed before it; a withdrawn start that lifts has those dates read again.
/// </summary>
[Service]
public class ListingSymbolStartService
{
    public const string EvidenceSource = "sec-cover-page-earlier-holder";

    private readonly EquityListingRepository _listings;
    private readonly EquityIssuerTickerEvidenceRepository _evidence;
    private readonly EquityListingSymbolStartRepository _starts;
    private readonly DailyShortVolumeRepository _shortVolumes;
    private readonly ShortInterestRepository _shortInterest;
    private readonly OffExchangeVolumeRepository _offExchangeVolumes;
    private readonly FinraImportPartitionRepository _partitions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ListingSymbolStartService> _logger;

    public ListingSymbolStartService(
        EquityListingRepository listings,
        EquityIssuerTickerEvidenceRepository evidence,
        EquityListingSymbolStartRepository starts,
        DailyShortVolumeRepository shortVolumes,
        ShortInterestRepository shortInterest,
        OffExchangeVolumeRepository offExchangeVolumes,
        FinraImportPartitionRepository partitions,
        TimeProvider timeProvider,
        ILogger<ListingSymbolStartService> logger
    )
    {
        _listings = listings;
        _evidence = evidence;
        _starts = starts;
        _shortVolumes = shortVolumes;
        _shortInterest = shortInterest;
        _offExchangeVolumes = offExchangeVolumes;
        _partitions = partitions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Re-derives every start from today's rows and evidence, stores the difference, and withdraws
    /// the rows filed before each start that held since the previous refresh.
    /// </summary>
    /// <returns>The number of listings with a recorded start after the refresh.</returns>
    public async Task<int> Refresh(CancellationToken cancellationToken)
    {
        var recorded = (
            await _starts.GetBySource(EvidenceSource).ToListAsync(cancellationToken)
        ).ToDictionary(row => (row.EquityListingId, row.Ticker));
        var claims = (
            await _evidence
                .GetAll()
                .GroupBy(row => new { row.Ticker, row.EquityIssuerId })
                .Select(group => new
                {
                    group.Key.Ticker,
                    group.Key.EquityIssuerId,
                    FirstFiled = group.Min(row => row.FiledDate),
                    LastFiled = group.Max(row => row.FiledDate),
                })
                .ToListAsync(cancellationToken)
        ).ToLookup(row => row.Ticker, StringComparer.Ordinal);
        var usListings = await _listings
            .GetAll()
            .Where(listing => listing.MarketCountryCode == "US")
            .Select(listing => new
            {
                listing.Id,
                listing.Security.EquityIssuerId,
                listing.Ticker,
                listing.Active,
            })
            .ToListAsync(cancellationToken);
        // Evidence keeps only the separator-free symbol, so ABC.U and ABCU read alike. An issuer whose
        // own lines spell the symbol differently from the candidate, and never like it, stated its
        // own line, not the candidate's.
        var spellings = usListings
            .Where(listing => TickerNormalizer.NormalizeIdentity(listing.Ticker) != null)
            .ToLookup(
                listing =>
                    (listing.EquityIssuerId, TickerNormalizer.NormalizeIdentity(listing.Ticker)),
                listing => TickerNormalizer.NormalizeDashListed(listing.Ticker)
            );

        var derived = new Dictionary<(Guid, string), ListingSymbolStartRule.Derivation>();
        // A withdrawn start on a retired listing waits unchanged until the listing trades again:
        // the imports map active listings only, so lifting it could never file its rows again.
        var retiredWithdrawn = usListings
            .Where(listing => !listing.Active)
            .Select(listing =>
                recorded.GetValueOrDefault((listing.Id, listing.Ticker))
                    is {
                        WithdrawnFrom: not null,
                        EarlierHolderFrom: { } from,
                        EarlierHolderThrough: { } through,
                    } start
                    ? (Start: start, Run: new ListingSymbolStartRule.RowRun(from, through))
                    : default
            )
            .Where(entry => entry.Start != null);
        foreach (var (start, run) in retiredWithdrawn)
        {
            derived[(start.EquityListingId, start.Ticker)] = new ListingSymbolStartRule.Derivation(
                start.StartsOn,
                run
            );
        }

        var candidates = usListings
            .Where(listing => listing.Active)
            .Select(listing =>
            {
                var symbol = TickerNormalizer.NormalizeIdentity(listing.Ticker) ?? "";
                var spelled = TickerNormalizer.NormalizeDashListed(listing.Ticker);
                return (
                    Listing: listing,
                    Claims: claims[symbol]
                        .Where(claim =>
                            !spellings.Contains((claim.EquityIssuerId, symbol))
                            || spellings[(claim.EquityIssuerId, symbol)].Contains(spelled)
                        )
                        .Select(claim => new ListingSymbolStartRule.SymbolClaim(
                            claim.EquityIssuerId,
                            claim.FirstFiled,
                            claim.LastFiled
                        ))
                        .ToList()
                );
            })
            .Where(candidate =>
                candidate.Claims.Any(claim =>
                    claim.EquityIssuerId != candidate.Listing.EquityIssuerId
                )
            );
        foreach (var (listing, symbolClaims) in candidates)
        {
            // Only the rows filed under the listing's current symbol: a renamed line keeps its
            // earlier symbol's rows, which say nothing about who held this one.
            var dates = await _shortVolumes
                .GetHistoryByListingId(listing.Id)
                .Where(row => row.ListedTicker == listing.Ticker)
                .OrderBy(row => row.Date)
                .Select(row => row.Date)
                .ToListAsync(cancellationToken);
            var derivation = recorded.GetValueOrDefault((listing.Id, listing.Ticker))
                is {
                    WithdrawnFrom: not null,
                    EarlierHolderFrom: { } from,
                    EarlierHolderThrough: { } through,
                } withdrawn
                ? ListingSymbolStartRule.Holds(
                    withdrawn.StartsOn,
                    new ListingSymbolStartRule.RowRun(from, through),
                    dates,
                    listing.EquityIssuerId,
                    symbolClaims
                )
                    ? new ListingSymbolStartRule.Derivation(
                        withdrawn.StartsOn,
                        new ListingSymbolStartRule.RowRun(from, through)
                    )
                    : null
                : ListingSymbolStartRule.Derive(dates, listing.EquityIssuerId, symbolClaims);
            if (derivation is not { } start)
                continue;

            derived[(listing.Id, listing.Ticker)] = start;
            _logger.LogInformation(
                "Symbol {Ticker} names listing {ListingId} from {StartsOn}; {Earlier} stored daily short-volume rows under it are dated earlier",
                listing.Ticker,
                listing.Id,
                start.StartsOn,
                dates.Count(date => date < start.StartsOn)
            );
        }

        await Store(derived, recorded.Values, cancellationToken);
        return derived.Count;
    }

    private async Task Store(
        IReadOnlyDictionary<(Guid, string), ListingSymbolStartRule.Derivation> derived,
        IReadOnlyCollection<EquityListingSymbolStart> stored,
        CancellationToken cancellationToken
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _starts.BeginTransaction(cancellationToken);

        // A start whose evidence went lifts. Its withdrawn dates sit under complete import markers,
        // so those markers reopen and the next imports file the dates under the listing again.
        var lifted = stored
            .Where(row => !derived.ContainsKey((row.EquityListingId, row.Ticker)))
            .ToList();
        foreach (var row in lifted)
            await Reopen(row, cancellationToken);
        _starts.Delete(lifted);

        // A start that moved waits a refresh before any withdrawal; one that held since the
        // previous refresh withdraws whatever is still filed before it.
        var held = new List<EquityListingSymbolStart>();
        foreach (
            var row in stored.Where(row => derived.ContainsKey((row.EquityListingId, row.Ticker)))
        )
        {
            var start = derived[(row.EquityListingId, row.Ticker)];
            if (row.StartsOn != start.StartsOn)
            {
                row.StartsOn = start.StartsOn;
                row.RecordedAt = now;
            }
            else
            {
                held.Add(row);
            }
            row.EarlierHolderFrom = start.EarlierHolder.Start;
            row.EarlierHolderThrough = start.EarlierHolder.End;
        }

        var storedKeys = stored.Select(row => (row.EquityListingId, row.Ticker)).ToHashSet();
        foreach (
            var ((listingId, ticker), start) in derived.Where(entry =>
                !storedKeys.Contains(entry.Key)
            )
        )
        {
            _starts.Add(
                new EquityListingSymbolStart
                {
                    EquityListingId = listingId,
                    Ticker = ticker,
                    StartsOn = start.StartsOn,
                    EarlierHolderFrom = start.EarlierHolder.Start,
                    EarlierHolderThrough = start.EarlierHolder.End,
                    EvidenceSource = EvidenceSource,
                    RecordedAt = now,
                }
            );
        }

        await _starts.SaveChanges();
        foreach (var row in held)
            await Withdraw(row, cancellationToken);
        await _starts.SaveChanges();
        await transaction.CommitAsync(cancellationToken);
    }

    // Only rows filed under the start's own symbol go: a renamed line's rows under its earlier
    // symbol are its own history. Each dataset goes by the date its import admits the listing on;
    // a week counts from its last day.
    private async Task Withdraw(EquityListingSymbolStart start, CancellationToken cancellationToken)
    {
        var lastWeekBefore = start.StartsOn.AddDays(-6);
        DateOnly?[] firsts =
        [
            await Withdraw(
                start,
                "daily short-volume",
                _shortVolumes
                    .GetHistoryByListingId(start.EquityListingId)
                    .Where(row => row.ListedTicker == start.Ticker && row.Date < start.StartsOn),
                row => row.Date,
                cancellationToken
            ),
            await Withdraw(
                start,
                "short-interest",
                _shortInterest
                    .GetHistoryByListingId(start.EquityListingId)
                    .Where(row =>
                        row.ListedTicker == start.Ticker && row.SettlementDate < start.StartsOn
                    ),
                row => row.SettlementDate,
                cancellationToken
            ),
            await Withdraw(
                start,
                "weekly off-exchange",
                _offExchangeVolumes
                    .GetHistoryByListingId(start.EquityListingId)
                    .Where(row =>
                        row.ListedTicker == start.Ticker && row.WeekStartDate < lastWeekBefore
                    ),
                row => row.WeekStartDate,
                cancellationToken
            ),
        ];
        // The first withdrawn date only moves earlier: the lift reopens from it.
        if (firsts.Min() is { } first && !(start.WithdrawnFrom <= first))
            start.WithdrawnFrom = first;
    }

    private async Task<DateOnly?> Withdraw<TRow>(
        EquityListingSymbolStart start,
        string dataset,
        IQueryable<TRow> rows,
        Expression<Func<TRow, DateOnly>> date,
        CancellationToken cancellationToken
    )
    {
        var dates = rows.Select(date);
        if (!await dates.AnyAsync(cancellationToken))
            return null;

        var first = await dates.MinAsync(cancellationToken);
        var withdrawn = await rows.ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation(
            "Withdrew {Count} {Dataset} rows filed under {Ticker} on listing {ListingId} before its symbol start {StartsOn}",
            withdrawn,
            dataset,
            start.Ticker,
            start.EquityListingId,
            start.StartsOn
        );
        return first;
    }

    private async Task Reopen(EquityListingSymbolStart start, CancellationToken cancellationToken)
    {
        if (start.WithdrawnFrom is not { } withdrawnFrom)
            return;

        // Short interest needs none: its import re-fetches every stored date's missing listings.
        // A weekly partition is keyed by its week's first day, which may fall before the first
        // withdrawn date; the import reads only the weeks FINRA still publishes.
        var through = start.StartsOn.AddDays(-1);
        var reopened = 0;
        foreach (
            var (dataset, from) in new[]
            {
                (ShortVolumeImportService.Dataset, withdrawnFrom),
                (OffExchangeVolumeImportService.Dataset, withdrawnFrom.AddDays(-6)),
            }
        )
        {
            foreach (
                var prefix in new[]
                {
                    FinraImportScope.ListingUniversePrefix,
                    FinraImportScope.ListingFilterPrefix,
                }
            )
            {
                reopened += await _partitions
                    .GetRangeByScopePrefix(dataset, prefix, from, through)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
        _logger.LogInformation(
            "Symbol start {StartsOn} of {Ticker} on listing {ListingId} lifted; reopened {Count} daily and weekly partitions from {WithdrawnFrom}",
            start.StartsOn,
            start.Ticker,
            start.EquityListingId,
            reopened,
            withdrawnFrom
        );
    }
}

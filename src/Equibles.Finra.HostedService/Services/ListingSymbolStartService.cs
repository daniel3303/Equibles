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
/// across two refreshes withdraws the daily short-volume rows already filed before it; a withdrawn
/// start that lifts has those dates' files read again.
/// </summary>
[Service]
public class ListingSymbolStartService
{
    public const string EvidenceSource = "sec-cover-page-earlier-holder";

    private readonly EquityListingRepository _listings;
    private readonly EquityIssuerTickerEvidenceRepository _evidence;
    private readonly EquityListingSymbolStartRepository _starts;
    private readonly DailyShortVolumeRepository _shortVolumes;
    private readonly FinraImportPartitionRepository _partitions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ListingSymbolStartService> _logger;

    public ListingSymbolStartService(
        EquityListingRepository listings,
        EquityIssuerTickerEvidenceRepository evidence,
        EquityListingSymbolStartRepository starts,
        DailyShortVolumeRepository shortVolumes,
        FinraImportPartitionRepository partitions,
        TimeProvider timeProvider,
        ILogger<ListingSymbolStartService> logger
    )
    {
        _listings = listings;
        _evidence = evidence;
        _starts = starts;
        _shortVolumes = shortVolumes;
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

        var derived = new Dictionary<(Guid, string), Derived>();
        // A withdrawn start stays under re-check after its listing retires: lifting it would only
        // have the whole market's files for its dates read again.
        var candidates = usListings
            .Where(listing =>
                listing.Active
                || recorded.GetValueOrDefault((listing.Id, listing.Ticker))?.WithdrawnFrom != null
            )
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

            var earlier = dates.Where(date => date < start.StartsOn).ToList();
            derived[(listing.Id, listing.Ticker)] = new Derived(
                start,
                earlier.Count > 0 ? earlier[0] : null
            );
            _logger.LogInformation(
                "Symbol {Ticker} names listing {ListingId} from {StartsOn}; {Earlier} stored daily short-volume rows under it are dated earlier",
                listing.Ticker,
                listing.Id,
                start.StartsOn,
                earlier.Count
            );
        }

        await Store(derived, recorded.Values, cancellationToken);
        return derived.Count;
    }

    // A derived start, and the first of the rows filed under its symbol before it.
    private readonly record struct Derived(
        ListingSymbolStartRule.Derivation Start,
        DateOnly? FirstEarlierRow
    );

    private async Task Store(
        IReadOnlyDictionary<(Guid, string), Derived> derived,
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
        foreach (var row in lifted.Where(row => row.WithdrawnFrom != null))
            await Reopen(row, cancellationToken);
        _starts.Delete(lifted);

        // A start that moved waits a refresh before any withdrawal; one that held since the
        // previous refresh marks its earlier rows withdrawn before they go.
        var withdrawals = new List<EquityListingSymbolStart>();
        foreach (
            var row in stored.Where(row => derived.ContainsKey((row.EquityListingId, row.Ticker)))
        )
        {
            var (start, firstEarlierRow) = derived[(row.EquityListingId, row.Ticker)];
            if (row.StartsOn != start.StartsOn)
            {
                row.StartsOn = start.StartsOn;
                row.RecordedAt = now;
            }
            else if (firstEarlierRow is { } first)
            {
                row.WithdrawnFrom =
                    row.WithdrawnFrom is { } withdrawnFrom && withdrawnFrom < first
                        ? withdrawnFrom
                        : first;
                withdrawals.Add(row);
            }
            row.EarlierHolderFrom = start.EarlierHolder.Start;
            row.EarlierHolderThrough = start.EarlierHolder.End;
        }

        var storedKeys = stored.Select(row => (row.EquityListingId, row.Ticker)).ToHashSet();
        foreach (
            var ((listingId, ticker), (start, _)) in derived.Where(entry =>
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
        foreach (var row in withdrawals)
            await Withdraw(row, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Only rows filed under the start's own symbol go: a renamed line's rows under its earlier
    // symbol are its own history.
    private async Task Withdraw(EquityListingSymbolStart start, CancellationToken cancellationToken)
    {
        var withdrawn = await _shortVolumes
            .GetHistoryByListingId(start.EquityListingId)
            .Where(row => row.ListedTicker == start.Ticker && row.Date < start.StartsOn)
            .ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation(
            "Withdrew {Count} daily short-volume rows filed under {Ticker} on listing {ListingId} before its symbol start {StartsOn}",
            withdrawn,
            start.Ticker,
            start.EquityListingId,
            start.StartsOn
        );
    }

    private async Task Reopen(EquityListingSymbolStart start, CancellationToken cancellationToken)
    {
        var through = start.StartsOn.AddDays(-1);
        var reopened = 0;
        foreach (
            var prefix in new[]
            {
                FinraImportScope.ListingUniversePrefix,
                FinraImportScope.ListingFilterPrefix,
            }
        )
        {
            reopened += await _partitions
                .GetRangeByScopePrefix(
                    ShortVolumeImportService.Dataset,
                    prefix,
                    start.WithdrawnFrom.Value,
                    through
                )
                .ExecuteDeleteAsync(cancellationToken);
        }
        _logger.LogInformation(
            "Symbol start {StartsOn} of {Ticker} on listing {ListingId} lifted; reopened {Count} daily short-volume partitions from {WithdrawnFrom}",
            start.StartsOn,
            start.Ticker,
            start.EquityListingId,
            reopened,
            start.WithdrawnFrom
        );
    }
}

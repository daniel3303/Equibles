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
/// files its earlier holder's history under the listing that holds it now.
/// </summary>
[Service]
public class ListingSymbolStartService
{
    public const string EvidenceSource = "sec-cover-page-earlier-holder";

    private readonly EquityListingRepository _listings;
    private readonly EquityIssuerTickerEvidenceRepository _evidence;
    private readonly EquityListingSymbolStartRepository _starts;
    private readonly DailyShortVolumeRepository _shortVolumes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ListingSymbolStartService> _logger;

    public ListingSymbolStartService(
        EquityListingRepository listings,
        EquityIssuerTickerEvidenceRepository evidence,
        EquityListingSymbolStartRepository starts,
        DailyShortVolumeRepository shortVolumes,
        TimeProvider timeProvider,
        ILogger<ListingSymbolStartService> logger
    )
    {
        _listings = listings;
        _evidence = evidence;
        _starts = starts;
        _shortVolumes = shortVolumes;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <returns>The number of listings with a recorded start after the refresh.</returns>
    public async Task<int> Refresh(CancellationToken cancellationToken)
    {
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

        var derived = new Dictionary<(Guid, string), DateOnly>();
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
            if (
                ListingSymbolStartRule.StartsOn(dates, listing.EquityIssuerId, symbolClaims) is
                { } start
            )
            {
                derived[(listing.Id, listing.Ticker)] = start;
                _logger.LogInformation(
                    "Symbol {Ticker} names listing {ListingId} from {StartsOn}; {Earlier} stored daily short-volume rows under it are dated earlier",
                    listing.Ticker,
                    listing.Id,
                    start,
                    dates.Count(date => date < start)
                );
            }
        }

        await Store(derived, cancellationToken);
        return derived.Count;
    }

    private async Task Store(
        IReadOnlyDictionary<(Guid, string), DateOnly> derived,
        CancellationToken cancellationToken
    )
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var stored = await _starts.GetBySource(EvidenceSource).ToListAsync(cancellationToken);
        // A start whose evidence went lifts; one whose evidence moved follows it.
        _starts.Delete(
            stored.Where(row => !derived.ContainsKey((row.EquityListingId, row.Ticker)))
        );
        foreach (
            var row in stored.Where(row =>
                derived.TryGetValue((row.EquityListingId, row.Ticker), out var startsOn)
                && row.StartsOn != startsOn
            )
        )
        {
            row.StartsOn = derived[(row.EquityListingId, row.Ticker)];
            row.RecordedAt = now;
        }

        var storedKeys = stored.Select(row => (row.EquityListingId, row.Ticker)).ToHashSet();
        foreach (
            var ((listingId, ticker), startsOn) in derived.Where(entry =>
                !storedKeys.Contains(entry.Key)
            )
        )
        {
            _starts.Add(
                new EquityListingSymbolStart
                {
                    EquityListingId = listingId,
                    Ticker = ticker,
                    StartsOn = startsOn,
                    EvidenceSource = EvidenceSource,
                    RecordedAt = now,
                }
            );
        }

        await _starts.SaveChanges();
    }
}

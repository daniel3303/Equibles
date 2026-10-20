using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services.Signals;

/// <summary>
/// The quarter-wide inputs of the stock signals: each issuer's shares outstanding as of the quarter
/// end on the quarter's basis, insiders' shares where a consolidated single-class count can divide
/// them, the Schedule 13D and 13G filers on record, the private-fund advisers and the top-scored
/// managers.
/// </summary>
internal static class StockHolderSignalsInputsLoader
{
    /// <summary>The fifth of scored managers by current three-year alpha whose trades are tracked.</summary>
    public const decimal TopScoredShare = 0.2m;

    private const int ChunkSize = 5_000;

    public static async Task<StockHolderSignalsQuarterInputs> LoadQuarter(
        EquiblesFinancialDbContext dbContext,
        SharesOutstandingAsOfReader sharesReader,
        DateOnly reportDate,
        IReadOnlyCollection<Guid> filers,
        bool measuresTopScored,
        CancellationToken cancellationToken
    )
    {
        var counts = await sharesReader.Read(reportDate, cancellationToken);
        var multiClass = await MultiClassIssuers(dbContext, cancellationToken);
        var earliestSplit = reportDate.AddMonths(-InsiderSharesAsOfLoader.MaximumAgeMonths);
        var issuerIds = counts.Keys.ToList();
        var splits = await PrimarySplits(
            dbContext,
            issuerIds,
            earliestSplit < reportDate.AddDays(-SharesOutstandingAsOfReader.MaximumAgeDays)
                ? earliestSplit
                : reportDate.AddDays(-SharesOutstandingAsOfReader.MaximumAgeDays),
            reportDate,
            cancellationToken
        );

        var shares = new Dictionary<Guid, (long, DateOnly, SharesOutstandingBasis)>(counts.Count);
        foreach (var (issuerId, count) in counts)
        {
            var basis =
                count.IsForeignForm ? SharesOutstandingBasis.ForeignOrdinary
                : count.IsClassSum || multiClass.Contains(issuerId)
                    ? SharesOutstandingBasis.ClassSum
                : SharesOutstandingBasis.Consolidated;
            var restated = SplitAdjustment.AdjustShareCount(
                count.Shares,
                count.AsOf,
                splits.GetValueOrDefault(issuerId) ?? []
            );
            shares[issuerId] = (restated, count.AsOf, basis);
        }

        var divisible = shares
            .Where(pair => pair.Value.Item3 == SharesOutstandingBasis.Consolidated)
            .Select(pair => pair.Key)
            .ToList();
        var institutionCiks = await FilerCiks(dbContext, filers, cancellationToken);
        var insiders = await InsiderSharesAsOfLoader.Load(
            dbContext,
            reportDate,
            divisible,
            institutionCiks,
            splits,
            cancellationToken
        );
        var beneficialOwnership = await Schedule13DGCountLoader.Load(
            dbContext,
            reportDate,
            cancellationToken
        );
        return new StockHolderSignalsQuarterInputs
        {
            SharesOutstanding = shares,
            InsiderShares = insiders,
            BeneficialOwnership = beneficialOwnership,
            MeasuresTopScored = measuresTopScored,
        };
    }

    /// <summary>Filers that advise private funds by their current Form ADV.</summary>
    public static async Task<HashSet<Guid>> PrivateFundAdvisers(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    ) =>
        (
            await dbContext
                .Set<InstitutionalHolderProfile>()
                .AsNoTracking()
                .Where(profile => profile.AdvisesPrivateFunds == true)
                .Select(profile => profile.InstitutionalHolderId)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

    /// <summary>
    /// The top fifth of managers with a current-version three-year score against the default
    /// benchmark, by alpha; empty when none is scored.
    /// </summary>
    public static async Task<HashSet<Guid>> TopScored(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var scores = await dbContext
            .Set<FundScore>()
            .AsNoTracking()
            .Where(score =>
                score.WindowYears == FundScoringManager.DefaultWindowYears
                && score.BenchmarkTicker == FundScoringManager.DefaultBenchmark
                && score.CalculationVersion == FundScore.CurrentCalculationVersion
            )
            .OrderByDescending(score => score.AlphaPercent)
            .ThenBy(score => score.InstitutionalHolderId)
            .Select(score => score.InstitutionalHolderId)
            .ToListAsync(cancellationToken);
        var take = (int)Math.Ceiling(scores.Count * TopScoredShare);
        return scores.Take(take).ToHashSet();
    }

    // A second class of common stock with an active US listing makes a cover-page count the
    // entity total across classes, as the unified holder list reads it.
    private static async Task<HashSet<Guid>> MultiClassIssuers(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    ) =>
        (
            await dbContext
                .Set<EquitySecurity>()
                .AsNoTracking()
                .Where(security =>
                    security.RegistrationType == ListedSecurityType.CommonShares
                    && security.Listings.Any(listing =>
                        listing.MarketCountryCode == "US" && listing.Active
                    )
                )
                .GroupBy(security => security.EquityIssuerId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

    // Each issuer's effective splits on its primary series dated in the window up to the quarter
    // end, so counts dated earlier restate onto the quarter's basis.
    private static async Task<Dictionary<Guid, List<StockSplit>>> PrimarySplits(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<Guid> issuerIds,
        DateOnly from,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var splits = new List<StockSplit>();
        foreach (var chunk in issuerIds.Chunk(ChunkSize))
            splits.AddRange(
                await StockSplitQueries
                    .ForIssuers(dbContext, chunk)
                    .AsNoTracking()
                    .Where(split => split.EffectiveDate > from && split.EffectiveDate <= reportDate)
                    .ToListAsync(cancellationToken)
            );
        if (splits.Count == 0)
            return [];
        var splitIssuers = splits.Select(split => split.EquityIssuerId).Distinct().ToList();
        var primaryTickers = await dbContext
            .Set<EquityIssuer>()
            .AsNoTracking()
            .Where(issuer => splitIssuers.Contains(issuer.Id))
            .Select(issuer => new { issuer.Id, issuer.Presentation.Listing.Ticker })
            .ToDictionaryAsync(issuer => issuer.Id, issuer => issuer.Ticker, cancellationToken);
        return splits
            .GroupBy(split => split.EquityIssuerId)
            .ToDictionary(
                group => group.Key,
                group =>
                    primaryTickers.GetValueOrDefault(group.Key) is { } ticker
                        ? ShareCountSplitScope.ForListedSeries(group, ticker, ticker)
                        : []
            );
    }

    private static async Task<HashSet<string>> FilerCiks(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<Guid> filers,
        CancellationToken cancellationToken
    )
    {
        var ciks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in filers.Chunk(ChunkSize))
        {
            var rows = await dbContext
                .Set<InstitutionalHolder>()
                .AsNoTracking()
                .Where(holder => chunk.Contains(holder.Id))
                .Select(holder => holder.Cik)
                .ToListAsync(cancellationToken);
            foreach (var cik in rows)
                if (CikNormalizer.Canonicalize(cik) is { } canonical)
                    ciks.Add(canonical);
        }
        return ciks;
    }
}

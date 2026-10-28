using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Moves stored 13F positions off a straddled split's later basis with the import's own
/// classifier (<see cref="SplitBasisShareCountRepairer"/>): the history imported before the
/// import judged it, and every position imported while its split still awaited its price
/// adjustment, when no basis could be judged yet.
/// </summary>
/// <remarks>
/// <para>
/// Walks every applied split with a wrap-around frontier and judges the issuer's positions whose
/// quarter ended shortly before the split and whose filing came after it. Only a position whose
/// figures disagree by the split's ratio loads its holder's neighbouring quarters, so a pass reads
/// candidates, not the issuer's whole book.
/// </para>
/// <para>
/// A corrected position is reset for honest repricing rather than valued here, so the recalculator
/// that runs next in the same cycle re-derives it under every valuation guard. A corrected count
/// or value agrees with the price again, so a later sweep leaves it alone.
/// </para>
/// </remarks>
[Service]
public class SplitBasisHoldingRepairService
{
    // The whole history holds about 2.3M straddling positions, so the first sweep takes about ten
    // passes and later sweeps read without writing.
    internal const int MaxCandidatesPerPass = 250_000;

    // The latest quarter a filing can straddle, with room for late filers and amendments.
    internal const int CandidateWindowDays = 100;

    private const int SplitPageSize = 1_000;

    private static Guid _frontier = Guid.Empty;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockPriceProvider _stockPriceProvider;
    private readonly ILogger<SplitBasisHoldingRepairService> _logger;

    public SplitBasisHoldingRepairService(
        IServiceScopeFactory scopeFactory,
        IStockPriceProvider stockPriceProvider,
        ILogger<SplitBasisHoldingRepairService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _stockPriceProvider = stockPriceProvider;
        _logger = logger;
    }

    internal static void ResetFrontier() => _frontier = Guid.Empty;

    // Guid.CompareTo is the only way to express the frontier in LINQ; Npgsql renders it as >.
    internal static IQueryable<SplitWindow> BuildSplitPageQuery(
        EquiblesFinancialDbContext dbContext,
        Guid frontier,
        DateOnly today
    ) =>
        StockSplitQueries
            .AppliedEffective(dbContext, today)
            .Where(split => split.Id.CompareTo(frontier) > 0)
            .OrderBy(split => split.Id)
            .Select(split => new SplitWindow
            {
                Id = split.Id,
                EquityIssuerId = split.EquityIssuerId,
                EffectiveDate = split.EffectiveDate,
            })
            .Take(SplitPageSize);

    internal static IQueryable<InstitutionalHolding> BuildCandidateQuery(
        EquiblesFinancialDbContext dbContext,
        Guid issuerId,
        DateOnly effectiveDate
    )
    {
        var earliest = effectiveDate.AddDays(-CandidateWindowDays);
        return dbContext
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Where(h =>
                h.EquityIssuerId == issuerId
                && h.ReportDate >= earliest
                && h.ReportDate < effectiveDate
                && h.FilingDate >= effectiveDate
                && h.FilingType == FilingType.Form13F
                && h.ShareType == ShareType.Shares
                && h.OptionType == null
                && h.Shares > 0
                && h.FiledValue > 0
            )
            .Select(h => new InstitutionalHolding
            {
                Id = h.Id,
                InstitutionalHolderId = h.InstitutionalHolderId,
                EquityIssuerId = h.EquityIssuerId,
                ListedTicker = h.ListedTicker,
                ReportDate = h.ReportDate,
                FilingDate = h.FilingDate,
                Shares = h.Shares,
                ShareType = h.ShareType,
                FiledValue = h.FiledValue,
                AccessionNumber = h.AccessionNumber,
                Cusip = h.Cusip,
            });
    }

    public async Task<int> Repair(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        if (dbContext.Database.IsRelational())
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var splits = await BuildSplitPageQuery(dbContext, _frontier, today)
            .ToListAsync(cancellationToken);
        var candidates = new Dictionary<Guid, InstitutionalHolding>();
        var walked = 0;
        foreach (var split in splits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (
                var holding in await BuildCandidateQuery(
                        dbContext,
                        split.EquityIssuerId,
                        split.EffectiveDate
                    )
                    .ToListAsync(cancellationToken)
            )
                candidates.TryAdd(holding.Id, holding);
            walked++;
            if (candidates.Count >= MaxCandidatesPerPass)
                break;
        }
        _frontier =
            walked == splits.Count && splits.Count < SplitPageSize
                ? Guid.Empty
                : splits[walked - 1].Id;
        if (candidates.Count == 0)
            return 0;

        var bases = await IssuerBases.Load(
            dbContext,
            candidates.Values.Select(h => h.EquityIssuerId).Distinct().ToList(),
            today,
            cancellationToken
        );
        var prices = await _stockPriceProvider.GetClosingPrices(
            candidates
                .Values.Select(h => (h.EquityIssuerId, h.ListedTicker, h.ReportDate))
                .Distinct()
                .ToList(),
            cancellationToken
        );

        // A candidate's straddled ratio and its quarter's price on the report-date basis, or null
        // when either cannot be stated honestly (the recalculator's own deferral gates).
        (decimal Factor, decimal Price)? ReportBasis(InstitutionalHolding holding)
        {
            if (
                !prices.TryGetValue(
                    (holding.EquityIssuerId, holding.ListedTicker, holding.ReportDate),
                    out var closePrice
                )
                || closePrice <= 0
                || HoldingValueSanityGuard.IsImplausibleClose(closePrice)
                || bases.FactorAt(holding, holding.ReportDate) is not { } reportFactor
                || HoldingValueSanityGuard.IsImplausibleClose(reportFactor * closePrice)
                || SplitBasisShareCountRepairer.StraddledFactor(
                    holding.ReportDate,
                    holding.FilingDate,
                    asOf => bases.FactorAt(holding, asOf)
                )
                    is not { } factor
            )
                return null;
            return (factor, reportFactor * closePrice);
        }

        var suspects = candidates
            .Values.Select(holding => (Holding: holding, Basis: ReportBasis(holding)))
            .Where(candidate =>
                candidate.Basis is (var factor, var price)
                && SplitBasisShareCountRepairer.Classify(
                    candidate.Holding.Shares,
                    candidate.Holding.FiledValue ?? 0L,
                    price,
                    factor,
                    neighbourShares: null
                ) != null
            )
            .Select(candidate =>
                (candidate.Holding, candidate.Basis.Value.Factor, candidate.Basis.Value.Price)
            )
            .ToList();
        if (suspects.Count == 0)
        {
            _logger.LogInformation(
                "Split-basis repair: judged {Candidates} straddling position(s) over {Splits} split(s); none sit on a later basis",
                candidates.Count,
                walked
            );
            return 0;
        }

        var judged = await KeepCorroboratedQuarters(dbContext, suspects, cancellationToken);
        var deferred = suspects.Count - judged.Count;
        var neighbourQuarters = await LoadNeighbourQuarters(
            dbContext,
            judged.Select(s => s.Holding).ToList(),
            cancellationToken
        );
        var corrections = new Dictionary<Guid, SplitBasisCorrection>();
        foreach (var (holding, factor, price) in judged)
        {
            var neighbour = SplitBasisShareCountRepairer.NearestNeighbour(
                holding.ReportDate,
                neighbourQuarters
                    .Where(q =>
                        q.InstitutionalHolderId == holding.InstitutionalHolderId
                        && q.EquityIssuerId == holding.EquityIssuerId
                        && q.AccessionNumber != holding.AccessionNumber
                        && string.Equals(
                            q.ListedTicker ?? "",
                            holding.ListedTicker ?? "",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Select(q => (q.ReportDate, q.Shares)),
                asOf => bases.FactorAt(holding, asOf)
            );
            if (
                SplitBasisShareCountRepairer.Classify(
                    holding.Shares,
                    holding.FiledValue ?? 0L,
                    price,
                    factor,
                    neighbour
                )
                is not { } kind
            )
                continue;
            corrections[holding.Id] = new SplitBasisCorrection(
                kind,
                factor,
                holding.AccessionNumber,
                holding.Cusip,
                holding.ListedTicker,
                holding.ReportDate,
                holding.FilingDate,
                holding.Shares,
                holding.FiledValue ?? 0L,
                neighbour
            );
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var ids = corrections.Keys.ToList();
        var rows = await dbContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h => ids.Contains(h.Id))
            .ToListAsync(cancellationToken);
        var corrected = new List<InstitutionalHolding>();
        foreach (var holding in rows)
        {
            var correction = corrections[holding.Id];
            // Re-read under the transaction: a concurrent import may already have rewritten it.
            if (
                holding.Shares != correction.FiledShares
                || (holding.FiledValue ?? 0L) != correction.FiledValue
            )
                continue;
            Apply(holding, correction);
            corrected.Add(holding);
        }
        await SplitBasisCorrectionEvidence.Stage(
            dbContext,
            corrected.Select(h => corrections[h.Id]).ToList(),
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);
        if (corrected.Count > 0)
        {
            await HoldingsRollupRefresher.Refresh(
                dbContext,
                corrected.Select(h => h.AccessionNumber).ToHashSet(),
                corrected.Select(h => h.ReportDate).ToHashSet(),
                cancellationToken
            );
        }
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);

        _logger.LogWarning(
            "Split-basis repair: judged {Candidates} straddling position(s) over {Splits} split(s); "
                + "rebased {Shares} share count(s) and {Values} filed value(s) for repricing, "
                + "deferred {Deferred} whose quarter does not confirm its price basis",
            candidates.Count,
            walked,
            corrected.Count(h =>
                corrections[h.Id].Kind == SplitBasisShareCountRepairer.Kind.PostSplitShares
            ),
            corrected.Count(h =>
                corrections[h.Id].Kind == SplitBasisShareCountRepairer.Kind.PostSplitValue
            ),
            deferred
        );
        return corrected.Count;
    }

    // Legs are updated in place, never cleared and re-added (manager-entry key budget).
    private static void Apply(InstitutionalHolding holding, SplitBasisCorrection correction)
    {
        var factor = correction.Factor;
        if (correction.Kind == SplitBasisShareCountRepairer.Kind.PostSplitShares)
        {
            holding.Shares = SplitBasisShareCountRepairer.Rebase(holding.Shares, factor);
            holding.VotingAuthSole = SplitBasisShareCountRepairer.Rebase(
                holding.VotingAuthSole,
                factor
            );
            holding.VotingAuthShared = SplitBasisShareCountRepairer.Rebase(
                holding.VotingAuthShared,
                factor
            );
            holding.VotingAuthNone = SplitBasisShareCountRepairer.Rebase(
                holding.VotingAuthNone,
                factor
            );
            foreach (var entry in holding.ManagerEntries)
                entry.Shares = SplitBasisShareCountRepairer.Rebase(entry.Shares, factor);
        }
        else
        {
            holding.FiledValue = SplitBasisShareCountRepairer.Revalue(
                correction.FiledValue,
                factor
            );
        }

        holding.Value = 0L;
        holding.ValuePending = true;
        holding.ValueUnavailable = false;
        holding.ValueRetryCount = 0;
        holding.ValueLastRetryAt = null;
        holding.ValueSource = ValueSource.Derived;
        foreach (var entry in holding.ManagerEntries)
            entry.Value = 0L;
    }

    // The quarter's other holders in the listing vouch for the price basis each suspect is judged
    // against; a quarter they do not confirm waits for a later sweep.
    private static async Task<
        List<(InstitutionalHolding Holding, decimal Factor, decimal Price)>
    > KeepCorroboratedQuarters(
        EquiblesFinancialDbContext dbContext,
        List<(InstitutionalHolding Holding, decimal Factor, decimal Price)> suspects,
        CancellationToken cancellationToken
    )
    {
        var kept = new List<(InstitutionalHolding Holding, decimal Factor, decimal Price)>();
        foreach (
            var quarter in suspects.GroupBy(suspect =>
                (
                    suspect.Holding.EquityIssuerId,
                    suspect.Holding.ListedTicker,
                    suspect.Holding.ReportDate
                )
            )
        )
        {
            var (issuerId, listedTicker, reportDate) = quarter.Key;
            var positions = await dbContext
                .Set<InstitutionalHolding>()
                .AsNoTracking()
                .Where(h =>
                    h.EquityIssuerId == issuerId
                    && h.ReportDate == reportDate
                    && h.ListedTicker == listedTicker
                    && h.FilingType == FilingType.Form13F
                    && h.ShareType == ShareType.Shares
                    && h.OptionType == null
                    && h.Shares > 0
                    && h.FiledValue > 0
                )
                .Select(h => new { h.Shares, h.FiledValue })
                .ToListAsync(cancellationToken);
            var (_, factor, price) = quarter.First();
            if (
                SplitBasisShareCountRepairer.PriceBasisCorroborated(
                    positions.Select(h => (h.Shares, h.FiledValue ?? 0L)),
                    price,
                    factor
                )
            )
                kept.AddRange(quarter);
        }
        return kept;
    }

    private static async Task<List<InstitutionalHolding>> LoadNeighbourQuarters(
        EquiblesFinancialDbContext dbContext,
        List<InstitutionalHolding> suspects,
        CancellationToken cancellationToken
    )
    {
        var window = SplitBasisShareCountRepairer.NeighbourWindowDays;
        var quarters = new List<InstitutionalHolding>();
        foreach (var issuer in suspects.GroupBy(h => h.EquityIssuerId))
        {
            var holderIds = issuer.Select(h => h.InstitutionalHolderId).Distinct().ToList();
            var from = issuer.Min(h => h.ReportDate).AddDays(-window);
            var to = issuer.Max(h => h.ReportDate).AddDays(window);
            quarters.AddRange(
                await dbContext
                    .Set<InstitutionalHolding>()
                    .AsNoTracking()
                    .Where(h =>
                        h.EquityIssuerId == issuer.Key
                        && holderIds.Contains(h.InstitutionalHolderId)
                        && h.ReportDate >= from
                        && h.ReportDate <= to
                        && h.FilingType == FilingType.Form13F
                        && h.ShareType == ShareType.Shares
                        && h.OptionType == null
                    )
                    .Select(h => new InstitutionalHolding
                    {
                        InstitutionalHolderId = h.InstitutionalHolderId,
                        EquityIssuerId = h.EquityIssuerId,
                        ListedTicker = h.ListedTicker,
                        ReportDate = h.ReportDate,
                        Shares = h.Shares,
                        AccessionNumber = h.AccessionNumber,
                    })
                    .ToListAsync(cancellationToken)
            );
        }
        return quarters;
    }

    internal sealed class SplitWindow
    {
        public Guid Id { get; init; }
        public Guid EquityIssuerId { get; init; }
        public DateOnly EffectiveDate { get; init; }
    }

    // Each issuer's effective splits and listing identity, the inputs HoldingValueBasis needs to
    // restate a count from any date onto today's basis.
    private sealed class IssuerBases
    {
        private Dictionary<Guid, List<StockSplit>> _splits;
        private Dictionary<Guid, string> _primaryTickers;
        private Dictionary<Guid, List<string>> _secondaryTickers;
        private Dictionary<Guid, IReadOnlyDictionary<string, string>> _seriesByTicker;

        public static async Task<IssuerBases> Load(
            EquiblesFinancialDbContext dbContext,
            List<Guid> issuerIds,
            DateOnly today,
            CancellationToken cancellationToken
        )
        {
            var splits = (
                await StockSplitQueries
                    .EffectiveForIssuers(dbContext, issuerIds, today)
                    .ToListAsync(cancellationToken)
            )
                .GroupBy(split => split.EquityIssuerId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var identities = await dbContext
                .Set<EquityIssuer>()
                .Where(cs => issuerIds.Contains(cs.Id))
                .Select(cs => new
                {
                    cs.Id,
                    Ticker = cs.Presentation.Listing.Ticker,
                    SecondaryTickers = cs
                        .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                        .Where(nativeListing =>
                            nativeListing.MarketCountryCode == "US"
                            && (
                                nativeListing.IsDirectoryListed
                                && nativeListing.Id != cs.Presentation.EquityListingId
                            )
                        )
                        .Select(nativeListing => nativeListing.Ticker)
                        .ToList(),
                })
                .ToListAsync(cancellationToken);
            return new IssuerBases
            {
                _splits = splits,
                _primaryTickers = identities.ToDictionary(cs => cs.Id, cs => cs.Ticker),
                _secondaryTickers = identities.ToDictionary(
                    cs => cs.Id,
                    cs => cs.SecondaryTickers ?? []
                ),
                _seriesByTicker = await RegisteredSeriesTickers.Load(
                    dbContext,
                    splits.Values.SelectMany(issuerSplits => issuerSplits),
                    cancellationToken
                ),
            };
        }

        public decimal? FactorAt(InstitutionalHolding holding, DateOnly asOf)
        {
            _splits.TryGetValue(holding.EquityIssuerId, out var splits);
            _primaryTickers.TryGetValue(holding.EquityIssuerId, out var primaryTicker);
            _secondaryTickers.TryGetValue(holding.EquityIssuerId, out var secondaryTickers);
            _seriesByTicker.TryGetValue(holding.EquityIssuerId, out var seriesByTicker);
            return HoldingValueBasis.TryResolveShareCountFactor(
                asOf,
                splits,
                holding.ListedTicker,
                primaryTicker,
                secondaryTickers,
                out var shareCountFactor,
                seriesByTicker
            )
                ? shareCountFactor
                : null;
        }
    }
}

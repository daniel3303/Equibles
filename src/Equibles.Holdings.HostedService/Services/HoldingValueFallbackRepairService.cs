using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Heals stored positions whose published value is a known lie: filed figures published on a
/// thousands basis, silent zeros the old retry ladder abandoned, and derivations inflated by a
/// corrupt close.
/// </summary>
/// <remarks>
/// <para>
/// Four populations, four phases:
/// <list type="number">
/// <item><b>Mis-published filed values</b> — rows a valuation decision site published as
/// <see cref="ValueSource.Filed"/> whose filed figure now looks thousands-scaled against an
/// available close (<see cref="HoldingValueSanityGuard.FiledLooksThousandsScaled"/>). The decision
/// is price-dependent, so it can be wrong in exactly one recoverable way: the price the decision
/// needed was missing or different when it ran (a price series still backfilling, a guard shipped
/// after the row was valued) and the row froze at the filer's thousands figure — 1,000× under
/// water — with nothing ever re-examining a settled Filed row. One production window minted 22k+
/// such rows on a single open quarter. The heal resets the row to pending so the recalculator
/// republishes the derivation under the shared guard — never publishing in-phase, which would be
/// a second implementation of the publish path free to drift from it.</item>
/// <item><b>Stuck zeros</b> — rows the repricing lane gave up on before the filed-value fallback
/// existed: <c>Value = 0</c>, not pending, not unavailable, with a perfectly good
/// <see cref="InstitutionalHolding.FiledValue"/> sitting unused in the same row. At its worst this
/// population was 48% of a whole quarter's positions, hiding real top holdings (a $11.9B NVDA
/// position sorted last) and understating every percentage. The heal publishes the filed figure.</item>
/// <item><b>Implausible derivations</b> — rows whose implied per-share price
/// (<c>Value / Shares</c>) exceeds <see cref="HoldingValueSanityGuard.MaxPlausibleSharePrice"/>.
/// One corrupt price series manufactured $102.8T of phantom value this way across 263 holders.
/// The heal resets them to pending so the recalculator re-derives them under its sanity guard
/// (which routes them to the filed value when the series is still corrupt).</item>
/// <item><b>Unmarked zeros</b> — the same abandoned population as phase 2 but with no filed
/// figure to publish. The ladder now stamps these <see cref="InstitutionalHolding.ValueUnavailable"/>
/// on exhaustion, but rows abandoned before that change are indistinguishable from "position
/// worth nothing", and every surface that discloses unvalued rows reads the flags. The heal
/// stamps them so one state has one representation.</item>
/// </list>
/// </para>
/// <para>
/// Phase 1 rules, each load-bearing:
/// <list type="bullet">
/// <item><b>Scope</b> is <c>ValueLastRetryAt == null</c> (with <c>Value == FiledValue</c> pinning
/// "the row still serves the filed figure" — the flush upsert historically did not carry
/// <see cref="ValueSource"/>, so a stale Filed label over a healthy derivation exists). The retry
/// ladder stamps <c>ValueLastRetryAt</c> on every advance, so ladder-exhaust publishes are outside
/// this phase by construction and remain the guard's documented accepted residual — a handful of
/// rows in production, priced never or too late, not the multi-million abandoned-zero population
/// (that one drained through phase 2 and carries a retry stamp anyway).</item>
/// <item><b>The thousands signature is corroborated per accession, never per row.</b> Thousands
/// reporting is a property of the filing's VALUE column: one in-band row alone can also be a
/// legitimately-Filed depositary row (~3,200× basis) whose stored price basis error happens to
/// land the recomputation inside the band — resetting that one would make the recalculator
/// publish a derivation ~1,000× the filer's own figure, terminally. A reset therefore requires
/// at least <see cref="MinCorroboratingRows"/> in-band rows making up at least
/// <see cref="CorroborationFraction"/> of the accession's priced rows in the batch (the
/// <c>Corrupt13FShareCountRepairer</c> pattern). Ambiguous in-band rows below that bar are
/// stamped instead — they keep serving the filer's own figure, and the quarterly bulk re-import
/// remains their healer.</item>
/// <item><b>Only a POSITIVE usable close examines a row.</b> The stamp is the one irreversible
/// decision in this lane (nothing but a re-import clears it), so it must never be taken on a
/// garbage price: a stored zero close derives 0, which is out-of-band, and would retire a
/// genuinely broken row forever. Zero/absent/implausible closes and unresolvable share-count
/// bases defer the row unstamped to a later cycle — the same deferral gates the recalculator
/// applies, so a reset row is always one it can republish.</item>
/// <item><b>The scan advances a wrap-around frontier</b> so permanently deferred rows (a price
/// series retired by the exact-listing cutover, an unresolvable secondary-listing split) cannot
/// pin the bounded window to the head of the Id order. Examined/stamped/deferred counts are
/// logged every non-empty cycle — a full window with a rising deferred count is the jam signal.</item>
/// </list>
/// </para>
/// <para>
/// Phases are isolated: one phase dying (the un-indexed scans routinely time out under
/// re-import load) must not starve the others, or a single slow predicate silently freezes every
/// heal at once — which is exactly how the thousands-scale population sat untouched while the
/// pass failed hourly on a later phase's timeout. Cancellation still propagates.
/// </para>
/// <para>
/// Bounded per cycle and self-terminating: a healed row no longer matches its phase's candidate
/// query (phase 1 drains through the reset or the <c>ValueLastRetryAt</c> stamp). Phase 1 cannot
/// ping-pong with the recalculator because both consult the same prices, the same share-count
/// factor and the same banded decision — a reset row either republishes as Derived (leaving the
/// Filed population) or re-exhausts into a retry stamp this phase excludes. Phase 3 terminates
/// because the recalculator guards the same number this phase tests — the effective per-share
/// price (factor × close) — so a reset row can never re-derive back above the cap and be reset
/// again. The stuck-zero phase is served by a partial Id worklist index whose entries disappear
/// as rows heal. The other candidate predicates are not index-served. Affected filing rollups and
/// AUM quarters are re-derived through
/// <see cref="HoldingsRollupRefresher"/> in the same pass — a healed position with a stale rollup
/// would just move the lie one aggregate up.
/// </para>
/// </remarks>
[Service]
public class HoldingValueFallbackRepairService
{
    // One cycle's ceiling per phase. The stuck-zero backlog is ~2.2M rows, so at the worker's
    // 24h cadence the drain takes ~44 daily cycles (about six weeks); the batch keeps each
    // SaveChanges tracked set and the rollup refresh bounded rather than materialising millions
    // of rows in one scope.
    internal const int MaxRowsPerCycle = 50_000;

    /// <summary>
    /// Minimum in-band rows an accession must show in the batch before any of them is reset —
    /// one or two can be coincidence (a depositary row behind a basis error), three sharing one
    /// filing cannot reasonably be.
    /// </summary>
    internal const int MinCorroboratingRows = 3;

    /// <summary>
    /// Minimum share of an accession's priced batch rows that must be in-band for a reset — a
    /// thousands-basis VALUE column marks (nearly) the whole filing, never a stray row of it.
    /// </summary>
    internal const decimal CorroborationFraction = 0.8m;

    // Wrap-around frontier for the phase-1 scan. Ids are client-generated Guids, so OrderBy(Id)
    // is an arbitrary but stable order; without the frontier, >MaxRowsPerCycle permanently
    // deferred residents would pin the window to the head forever. Process restarts reset it to
    // the head, which only costs re-examining already-stamped rows' predicate (they no longer
    // match).
    private static Guid _reviseFrontier = Guid.Empty;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockPriceProvider _stockPriceProvider;
    private readonly ILogger<HoldingValueFallbackRepairService> _logger;

    public HoldingValueFallbackRepairService(
        IServiceScopeFactory scopeFactory,
        IStockPriceProvider stockPriceProvider,
        ILogger<HoldingValueFallbackRepairService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _stockPriceProvider = stockPriceProvider;
        _logger = logger;
    }

    public async Task<int> Repair(CancellationToken cancellationToken)
    {
        var revisedFiled = await RunPhase("revise-filed", ReviseFiledPublishes, cancellationToken);
        var healedZeros = await RunPhase("stuck-zeros", HealStuckZeros, cancellationToken);
        var resetImplausible = await RunPhase(
            "implausible-derivations",
            ResetImplausibleDerivations,
            cancellationToken
        );
        var markedUnavailable = await RunPhase(
            "unmarked-zeros",
            MarkAbandonedZerosUnavailable,
            cancellationToken
        );

        if (revisedFiled > 0 || healedZeros > 0 || resetImplausible > 0 || markedUnavailable > 0)
        {
            _logger.LogWarning(
                "Value fallback repair: reset {RevisedFiled} thousands-scale filed publish(es) "
                    + "for honest repricing, published the filed value on {HealedZeros} abandoned "
                    + "zero-value position(s), reset {ResetImplausible} implausibly-derived "
                    + "position(s) for honest repricing, and marked {MarkedUnavailable} "
                    + "filed-value-less abandoned zero(s) as unavailable",
                revisedFiled,
                healedZeros,
                resetImplausible,
                markedUnavailable
            );
        }

        return revisedFiled + healedZeros + resetImplausible + markedUnavailable;
    }

    // One phase's failure must not starve the phases after it; only cancellation propagates.
    private async Task<int> RunPhase(
        string phase,
        Func<CancellationToken, Task<int>> heal,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await heal(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Value fallback repair phase {Phase} failed; continuing with the remaining phases",
                phase
            );
            return 0;
        }
    }

    // Exposed for the Npgsql translation pin: Guid.CompareTo is the only way to express the
    // frontier in LINQ (Guid has no comparison operators), and an untranslatable shape would
    // pass every InMemory-backed test while faulting the phase at runtime.
    internal static IQueryable<InstitutionalHolding> BuildReviseCandidateQuery(
        EquiblesFinancialDbContext dbContext,
        Guid frontier
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h =>
                !h.ValuePending
                && !h.ValueUnavailable
                && h.ValueSource == ValueSource.Filed
                && h.ValueLastRetryAt == null
                && h.FiledValue != null
                && h.FiledValue > 0
                && h.Value == h.FiledValue
                && h.Shares > 0
                && h.Id.CompareTo(frontier) > 0
            )
            .OrderBy(h => h.Id)
            .Take(MaxRowsPerCycle);

    /// <summary>
    /// Re-examines decision-site filed publishes against the prices available now, resetting to
    /// pending those whose filed figure is on a corroborated thousands basis.
    /// </summary>
    private async Task<int> ReviseFiledPublishes(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        ExtendCommandTimeout(dbContext);

        var rows = await BuildReviseCandidateQuery(dbContext, _reviseFrontier)
            .ToListAsync(cancellationToken);

        _reviseFrontier = rows.Count == MaxRowsPerCycle ? rows[^1].Id : Guid.Empty;

        if (rows.Count == 0)
        {
            return 0;
        }

        var pairs = rows.Select(h => (h.CommonStockId, h.ListedTicker, h.ReportDate))
            .Distinct()
            .ToList();
        var prices = await _stockPriceProvider.GetClosingPrices(pairs, cancellationToken);

        var stockIds = rows.Select(h => h.CommonStockId).Distinct().ToList();
        var splitsByStock = (
            await dbContext
                .Set<StockSplit>()
                .Where(s => stockIds.Contains(s.CommonStockId))
                .ToListAsync(cancellationToken)
        )
            .GroupBy(s => s.CommonStockId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var tickerIdentities = await dbContext
            .Set<CommonStock>()
            .Where(cs => stockIds.Contains(cs.Id))
            .Select(cs => new
            {
                cs.Id,
                cs.Ticker,
                cs.SecondaryTickers,
            })
            .ToListAsync(cancellationToken);
        var primaryTickers = tickerIdentities.ToDictionary(cs => cs.Id, cs => cs.Ticker);
        var secondaryTickers = tickerIdentities.ToDictionary(
            cs => cs.Id,
            cs => cs.SecondaryTickers ?? []
        );

        var deferred = 0;
        var priced = new List<(InstitutionalHolding Holding, bool InBand)>();

        foreach (var holding in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only a POSITIVE plausible close examines a row: the stamp below is irreversible,
            // and a zero/corrupt close derives an out-of-band figure that would retire a
            // genuinely broken row forever. These are the recalculator's own deferral gates, so
            // a reset row is always one it can republish.
            if (
                !prices.TryGetValue(
                    (holding.CommonStockId, holding.ListedTicker, holding.ReportDate),
                    out var closePrice
                )
                || closePrice <= 0
                || HoldingValueSanityGuard.IsImplausibleClose(closePrice)
            )
            {
                deferred++;
                continue;
            }

            splitsByStock.TryGetValue(holding.CommonStockId, out var splits);
            primaryTickers.TryGetValue(holding.CommonStockId, out var primaryTicker);
            secondaryTickers.TryGetValue(holding.CommonStockId, out var listedSecondaries);
            if (
                !HoldingValueBasis.TryResolveShareCountFactor(
                    holding.ReportDate,
                    splits,
                    holding.ListedTicker,
                    primaryTicker,
                    listedSecondaries,
                    out var shareCountFactor
                ) || HoldingValueSanityGuard.IsImplausibleClose(shareCountFactor * closePrice)
            )
            {
                deferred++;
                continue;
            }

            var derived = holding.Shares * shareCountFactor * closePrice;
            priced.Add(
                (
                    holding,
                    HoldingValueSanityGuard.FiledLooksThousandsScaled(
                        derived,
                        holding.Shares,
                        holding.FiledValue
                    )
                )
            );
        }

        var examinedAt = DateTime.UtcNow;
        var resetRows = new List<InstitutionalHolding>();
        var stamped = 0;

        foreach (var filing in priced.GroupBy(p => p.Holding.AccessionNumber))
        {
            var pricedCount = filing.Count();
            var inBandCount = filing.Count(p => p.InBand);
            var corroborated =
                inBandCount >= MinCorroboratingRows
                && inBandCount >= CorroborationFraction * pricedCount;

            foreach (var (holding, inBand) in filing)
            {
                if (corroborated && inBand)
                {
                    holding.Value = 0L;
                    holding.ValuePending = true;
                    holding.ValueRetryCount = 0;
                    holding.ValueLastRetryAt = null;
                    holding.ValueSource = ValueSource.Derived;
                    foreach (var entry in holding.ManagerEntries)
                    {
                        entry.Value = 0L;
                    }
                    resetRows.Add(holding);
                }
                else
                {
                    // Examined under a usable price and either legitimately filed (depositary
                    // basis, options premium) or in-band without corroboration: the stamp
                    // retires it from this candidate set; a stamped ambiguous row keeps serving
                    // the filer's own figure until a bulk re-import re-decides it.
                    holding.ValueLastRetryAt = examinedAt;
                    stamped++;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (resetRows.Count > 0)
        {
            await HoldingsRollupRefresher.Refresh(
                dbContext,
                resetRows.Select(h => h.AccessionNumber).ToHashSet(),
                resetRows.Select(h => h.ReportDate).ToHashSet(),
                cancellationToken
            );
        }

        // A full window with a rising deferred count is the jam signal; without these numbers
        // "drained" and "jammed on permanent residents" read identically in the logs.
        _logger.LogInformation(
            "Filed-publish revision: examined {Examined} candidate(s), reset {Reset}, "
                + "retired {Stamped}, deferred {Deferred} awaiting a usable price",
            rows.Count,
            resetRows.Count,
            stamped,
            deferred
        );

        return resetRows.Count;
    }

    /// <summary>
    /// Publishes the filed value on rows the old ladder abandoned at <c>Value = 0</c>.
    /// </summary>
    internal static IQueryable<InstitutionalHolding> BuildStuckZeroCandidateQuery(
        EquiblesFinancialDbContext dbContext
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h =>
                h.Value == 0L
                && !h.ValuePending
                && !h.ValueUnavailable
                && h.FiledValue != null
                && h.FiledValue > 0
            )
            .OrderBy(h => h.Id)
            .Take(MaxRowsPerCycle);

    private async Task<int> HealStuckZeros(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        ExtendCommandTimeout(dbContext);

        var rows = await BuildStuckZeroCandidateQuery(dbContext).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var holding in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HoldingsValueRecalculator.ApplyFiledValue(holding);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await HoldingsRollupRefresher.Refresh(
            dbContext,
            rows.Select(h => h.AccessionNumber).ToHashSet(),
            rows.Select(h => h.ReportDate).ToHashSet(),
            cancellationToken
        );

        return rows.Count;
    }

    /// <summary>
    /// Resets rows whose implied per-share price is impossible, so the recalculator re-derives
    /// them under its sanity guard.
    /// </summary>
    private async Task<int> ResetImplausibleDerivations(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        ExtendCommandTimeout(dbContext);

        // Decimal math server-side: 1M × a large share count overflows Int64, and the point of
        // the predicate is exactly the rows where Value is astronomically large. Filed-value rows
        // are excluded — that figure is the filer's own claim, not our derivation error, and
        // resetting one would loop it through the fallback forever.
        var rows = await dbContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h =>
                !h.ValuePending
                && h.ValueSource != ValueSource.Filed
                && h.Shares > 0
                && (decimal)h.Value > HoldingValueSanityGuard.MaxPlausibleSharePrice * h.Shares
            )
            .OrderBy(h => h.Id)
            .Take(MaxRowsPerCycle)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var holding in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            holding.Value = 0L;
            holding.ValuePending = true;
            holding.ValueRetryCount = 0;
            holding.ValueLastRetryAt = null;
            holding.ValueSource = ValueSource.Derived;

            foreach (var entry in holding.ManagerEntries)
            {
                entry.Value = 0L;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await HoldingsRollupRefresher.Refresh(
            dbContext,
            rows.Select(h => h.AccessionNumber).ToHashSet(),
            rows.Select(h => h.ReportDate).ToHashSet(),
            cancellationToken
        );

        return rows.Count;
    }

    /// <summary>
    /// Stamps <see cref="InstitutionalHolding.ValueUnavailable"/> on zeros the old ladder
    /// abandoned with no filed figure, so "unknown" stops masquerading as "worth nothing".
    /// </summary>
    private async Task<int> MarkAbandonedZerosUnavailable(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        ExtendCommandTimeout(dbContext);

        // Value stays 0, so no rollup or AUM figure moves — the stamp only makes the row
        // visible to every surface that discloses unvalued positions through the flags.
        var rows = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h =>
                h.Value == 0L
                && !h.ValuePending
                && !h.ValueUnavailable
                && (h.FiledValue == null || h.FiledValue <= 0)
                && h.ValueRetryCount > 0
            )
            .OrderBy(h => h.Id)
            .Take(MaxRowsPerCycle)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var holding in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            holding.ValueUnavailable = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private static void ExtendCommandTimeout(EquiblesFinancialDbContext dbContext)
    {
        if (dbContext.Database.IsRelational())
        {
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }
    }
}

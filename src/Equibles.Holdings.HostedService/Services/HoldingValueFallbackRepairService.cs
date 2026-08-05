using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Heals stored positions whose published value is a known lie: silent zeros the old retry ladder
/// abandoned, and derivations inflated by a corrupt close.
/// </summary>
/// <remarks>
/// <para>
/// Three populations, three phases:
/// <list type="number">
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
/// <item><b>Unmarked zeros</b> — the same abandoned population as phase 1 but with no filed
/// figure to publish. The ladder now stamps these <see cref="InstitutionalHolding.ValueUnavailable"/>
/// on exhaustion, but rows abandoned before that change are indistinguishable from "position
/// worth nothing", and every surface that discloses unvalued rows reads the flags. The heal
/// stamps them so one state has one representation.</item>
/// </list>
/// </para>
/// <para>
/// Bounded per cycle and self-terminating: a healed row no longer matches its phase's candidate
/// query. Phase 2 terminates because the recalculator guards the same number this phase tests —
/// the effective per-share price (factor × close) — so a reset row either re-derives under the
/// cap, falls back to the filed value, or exhausts the ladder into <c>ValueUnavailable</c>; it
/// can never re-derive back above the cap and be reset again. Neither candidate predicate is
/// index-served (the zero test isn't selective enough to earn one and the per-share comparison
/// isn't sargable), so a drained backlog still costs two bounded sequential scans per daily
/// cycle — accepted for a 24h cadence. Affected filing rollups and AUM quarters are re-derived
/// through <see cref="HoldingsRollupRefresher"/> in the same pass — a healed position with a
/// stale rollup would just move the lie one aggregate up.
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

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingValueFallbackRepairService> _logger;

    public HoldingValueFallbackRepairService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingValueFallbackRepairService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> Repair(CancellationToken cancellationToken)
    {
        var healedZeros = await HealStuckZeros(cancellationToken);
        var resetImplausible = await ResetImplausibleDerivations(cancellationToken);
        var markedUnavailable = await MarkAbandonedZerosUnavailable(cancellationToken);

        if (healedZeros > 0 || resetImplausible > 0 || markedUnavailable > 0)
        {
            _logger.LogWarning(
                "Value fallback repair: published the filed value on {HealedZeros} abandoned "
                    + "zero-value position(s), reset {ResetImplausible} implausibly-derived "
                    + "position(s) for honest repricing, and marked {MarkedUnavailable} "
                    + "filed-value-less abandoned zero(s) as unavailable",
                healedZeros,
                resetImplausible,
                markedUnavailable
            );
        }

        return healedZeros + resetImplausible + markedUnavailable;
    }

    /// <summary>
    /// Publishes the filed value on rows the old ladder abandoned at <c>Value = 0</c>.
    /// </summary>
    private async Task<int> HealStuckZeros(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        if (dbContext.Database.IsRelational())
        {
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }

        var rows = await dbContext
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
            .Take(MaxRowsPerCycle)
            .ToListAsync(cancellationToken);

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
        if (dbContext.Database.IsRelational())
        {
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }

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
        if (dbContext.Database.IsRelational())
        {
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }

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
}

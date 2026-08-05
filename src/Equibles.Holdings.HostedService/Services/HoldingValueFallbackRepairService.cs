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
/// Two populations, two phases:
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
/// </list>
/// </para>
/// <para>
/// Bounded per cycle and self-terminating: a healed row no longer matches its phase's candidate
/// query, so once the backlog drains each pass costs one cheap indexed query. Affected filing
/// rollups and AUM quarters are re-derived through <see cref="HoldingsRollupRefresher"/> in the
/// same pass — a healed position with a stale rollup would just move the lie one aggregate up.
/// </para>
/// </remarks>
[Service]
public class HoldingValueFallbackRepairService
{
    // One cycle's ceiling per phase. The stuck-zero backlog is ~2.2M rows, so the drain takes a
    // few daily cycles; the batch keeps each SaveChanges tracked set and the rollup refresh
    // bounded rather than materialising millions of rows in one scope.
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

        if (healedZeros > 0 || resetImplausible > 0)
        {
            _logger.LogWarning(
                "Value fallback repair: published the filed value on {HealedZeros} abandoned "
                    + "zero-value position(s) and reset {ResetImplausible} implausibly-derived "
                    + "position(s) for honest repricing",
                healedZeros,
                resetImplausible
            );
        }

        return healedZeros + resetImplausible;
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
                && (decimal)h.Value
                    > HoldingValueSanityGuard.MaxPlausibleSharePrice * h.Shares
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
}

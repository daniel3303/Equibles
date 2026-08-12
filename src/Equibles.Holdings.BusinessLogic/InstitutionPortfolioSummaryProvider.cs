using System.Globalization;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.Holdings.BusinessLogic;

// One shared implementation for MCP and REST portfolio summaries. Closed-quarter results are
// pure functions of two holder snapshots, so their ComputedAt values form the cache version.
// Dirty quarters bypass the cache until the aggregate drain finishes; this prevents a filing
// import from serving the previous summary for the cache TTL.
[Service]
public class InstitutionPortfolioSummaryProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly IMemoryCache _memoryCache;

    public InstitutionPortfolioSummaryProvider(
        InstitutionalHoldingRepository holdingRepository,
        IMemoryCache memoryCache
    )
    {
        _holdingRepository = holdingRepository;
        _memoryCache = memoryCache;
    }

    public async Task<InstitutionPortfolioSummary> Get(
        InstitutionalHolder holder,
        DateOnly currentReportDate,
        DateOnly? previousReportDate,
        int quartersReported,
        CancellationToken cancellationToken = default
    )
    {
        var state = await _holdingRepository.GetHolderSummarySnapshotState(
            holder.Id,
            currentReportDate,
            previousReportDate,
            cancellationToken
        );
        if (!state.CanCache(previousReportDate.HasValue))
        {
            return await Calculate(
                holder,
                currentReportDate,
                previousReportDate,
                quartersReported,
                cancellationToken
            );
        }

        var key = string.Join(
            ':',
            "holdings",
            "institution-summary",
            holder.Id.ToString("N"),
            currentReportDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            previousReportDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "none",
            quartersReported.ToString(CultureInfo.InvariantCulture),
            state.CurrentComputedAt!.Value.Ticks.ToString(CultureInfo.InvariantCulture),
            state.PreviousComputedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "none"
        );
        return await _memoryCache.GetOrCreateAsync(
            key,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await Calculate(
                    holder,
                    currentReportDate,
                    previousReportDate,
                    quartersReported,
                    cancellationToken
                );
            }
        );
    }

    private async Task<InstitutionPortfolioSummary> Calculate(
        InstitutionalHolder holder,
        DateOnly currentReportDate,
        DateOnly? previousReportDate,
        int quartersReported,
        CancellationToken cancellationToken
    )
    {
        var current = await _holdingRepository
            .Get13FPositionAggregatesByHolder(holder, currentReportDate)
            .ToListAsync(cancellationToken);
        var previous = previousReportDate is { } prior
            ? await _holdingRepository
                .Get13FPositionAggregatesByHolder(holder, prior)
                .ToListAsync(cancellationToken)
            : [];
        return InstitutionPortfolioSummaryCalculator.CalculatePositions(
            current,
            previous,
            quartersReported,
            currentReportDate,
            previousReportDate
        );
    }
}

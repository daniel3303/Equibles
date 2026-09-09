using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

public static class HoldingsComparisonCoverage
{
    public static async Task<Dictionary<DateOnly, HoldingsComparisonStatus>> History(
        InstitutionalHoldingRepository repository,
        CommonStock stock,
        IReadOnlyList<DateOnly> dates,
        string listedTicker = null,
        CancellationToken cancellationToken = default
    )
    {
        var ordered = dates.Distinct().Order().ToArray();
        if (ordered.Length < 2)
            return [];
        var holdings =
            listedTicker == null
                ? repository.Get13FHistoryByStock(stock)
                : repository.Get13FHistoryByListing(stock, listedTicker);
        var positions = await holdings
            .Where(h => ordered.Contains(h.ReportDate))
            .Select(h => new HoldingsFilingPresence
            {
                ReportDate = h.ReportDate,
                InstitutionalHolderId = h.InstitutionalHolderId,
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        var byDate = positions
            .GroupBy(p => p.ReportDate)
            .ToDictionary(g => g.Key, g => g.Select(p => p.InstitutionalHolderId).ToHashSet());
        var pending = new List<HoldingsComparisonStatus>();
        for (var index = 1; index < ordered.Length; index++)
        {
            var current = ordered[index];
            var previous = ordered[index - 1];
            pending.Add(
                Compare(
                    current,
                    previous,
                    byDate.GetValueOrDefault(current) ?? [],
                    byDate.GetValueOrDefault(previous) ?? []
                )
            );
        }
        return await Resolve(repository, pending, cancellationToken);
    }

    public static async Task<HoldingsComparisonStatus> Activity(
        InstitutionalHoldingRepository repository,
        IReadOnlyCollection<HolderStockActivity> activity,
        DateOnly current,
        DateOnly previous,
        CancellationToken cancellationToken = default
    )
    {
        var pending = Compare(
            current,
            previous,
            activity
                .Where(row => row.CurrentPositionCount > 0)
                .Select(row => row.InstitutionalHolderId)
                .ToHashSet(),
            activity
                .Where(row => row.PreviousPositionCount > 0)
                .Select(row => row.InstitutionalHolderId)
                .ToHashSet()
        );
        return (await Resolve(repository, [pending], cancellationToken))[current];
    }

    private static HoldingsComparisonStatus Compare(
        DateOnly current,
        DateOnly previous,
        HashSet<Guid> currentHolders,
        HashSet<Guid> previousHolders
    ) =>
        new(
            current,
            previous,
            currentHolders.Except(previousHolders).ToHashSet(),
            previousHolders.Except(currentHolders).ToHashSet()
        );

    private static async Task<Dictionary<DateOnly, HoldingsComparisonStatus>> Resolve(
        InstitutionalHoldingRepository repository,
        IReadOnlyList<HoldingsComparisonStatus> pending,
        CancellationToken cancellationToken
    )
    {
        // Read evidence for every missing side in one command; per-quarter reads amplify
        // the cost of long ownership histories. Same-stock rows already prove filing presence.
        var requested = pending
            .Where(p => p.ConsecutiveQuarters)
            .SelectMany(p =>
                p.MissingPreviousFilers.Select(id => new HoldingsFilingPresence
                    {
                        ReportDate = p.PreviousReportDate,
                        InstitutionalHolderId = id,
                    })
                    .Concat(
                        p.MissingCurrentFilers.Select(id => new HoldingsFilingPresence
                        {
                            ReportDate = p.ReportDate,
                            InstitutionalHolderId = id,
                        })
                    )
            )
            .Distinct()
            .ToList();
        var query = EvidenceQuery(repository, requested);
        var observed = query == null ? [] : await query.ToListAsync(cancellationToken);
        var byDate = observed.ToLookup(p => p.ReportDate, p => p.InstitutionalHolderId);
        return pending.ToDictionary(
            p => p.ReportDate,
            p =>
                p with
                {
                    MissingPreviousFilers = p
                        .MissingPreviousFilers.Except(byDate[p.PreviousReportDate])
                        .ToHashSet(),
                    MissingCurrentFilers = p
                        .MissingCurrentFilers.Except(byDate[p.ReportDate])
                        .ToHashSet(),
                }
        );
    }

    internal static IQueryable<HoldingsFilingPresence> EvidenceQuery(
        InstitutionalHoldingRepository repository,
        IReadOnlyList<HoldingsFilingPresence> requested
    )
    {
        IQueryable<HoldingsFilingPresence> query = null;
        foreach (var group in requested.GroupBy(p => p.ReportDate))
        {
            var date = group.Key;
            var holders = group.Select(p => p.InstitutionalHolderId).Distinct().ToArray();
            var branch = repository
                .GetAll()
                .Where(h =>
                    h.FilingType == FilingType.Form13F
                    && h.ReportDate == date
                    && holders.Contains(h.InstitutionalHolderId)
                )
                .Select(h => new HoldingsFilingPresence
                {
                    ReportDate = h.ReportDate,
                    InstitutionalHolderId = h.InstitutionalHolderId,
                })
                .Distinct();
            query = query == null ? branch : query.Concat(branch);
        }
        return query;
    }
}

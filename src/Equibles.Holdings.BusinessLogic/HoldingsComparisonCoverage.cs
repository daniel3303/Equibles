using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
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
        EquityIssuer stock,
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<StockSplit> splits,
        string listedTicker = null,
        DateOnly? combinedReportDate = null,
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
        var resolved = await Resolve(repository, pending, cancellationToken);
        // The combined view carries every holder yet to file at its prior position, so those
        // positions sit in both totals and are compared, not excluded.
        if (combinedReportDate is { } combined && resolved.TryGetValue(combined, out var carried))
            resolved[combined] = carried with { MissingCurrentFilers = new HashSet<Guid>() };
        return await WithExcludedFilers(holdings, stock, resolved, splits, cancellationToken);
    }

    // Loads each missing filer's shares on the side where it holds the stock, from the same rows
    // and split basis as the totals, so the like-for-like change subtracts like from like.
    private static async Task<Dictionary<DateOnly, HoldingsComparisonStatus>> WithExcludedFilers(
        IQueryable<InstitutionalHolding> holdings,
        EquityIssuer stock,
        Dictionary<DateOnly, HoldingsComparisonStatus> statuses,
        IReadOnlyList<StockSplit> splits,
        CancellationToken cancellationToken
    )
    {
        var requested = statuses
            .Values.Where(status => status.ConsecutiveQuarters)
            .SelectMany(status =>
                status
                    .MissingPreviousFilers.Select(id => (Date: status.ReportDate, Holder: id))
                    .Concat(
                        status.MissingCurrentFilers.Select(id =>
                            (Date: status.PreviousReportDate, Holder: id)
                        )
                    )
            )
            .Distinct()
            .ToList();
        if (requested.Count == 0)
            return statuses;

        IQueryable<ExcludedPosition> query = null;
        foreach (var group in requested.GroupBy(request => request.Date))
        {
            var date = group.Key;
            var holders = group.Select(request => request.Holder).ToArray();
            var branch = holdings
                .Where(h => h.ReportDate == date && holders.Contains(h.InstitutionalHolderId))
                .GroupBy(h => new
                {
                    h.ReportDate,
                    h.InstitutionalHolderId,
                    h.InstitutionalHolder.Name,
                    h.ListedTicker,
                })
                .Select(g => new ExcludedPosition
                {
                    ReportDate = g.Key.ReportDate,
                    InstitutionalHolderId = g.Key.InstitutionalHolderId,
                    Name = g.Key.Name,
                    ListedTicker = g.Key.ListedTicker,
                    Shares = g.Sum(h => h.Shares),
                });
            query = query == null ? branch : query.Concat(branch);
        }
        var positions = await query.ToListAsync(cancellationToken);
        var primaryTicker = stock.Presentation.Listing.Ticker;
        var filers = positions
            .GroupBy(position => (position.ReportDate, position.InstitutionalHolderId))
            .ToDictionary(
                group => group.Key,
                group => new HoldingsExcludedFiler(
                    group.Key.InstitutionalHolderId,
                    group.First().Name,
                    group.Sum(position =>
                        SplitAdjustment.AdjustShareCount(
                            position.Shares,
                            position.ReportDate,
                            ShareCountSplitScope.ForListedSeries(
                                splits,
                                primaryTicker,
                                position.ListedTicker ?? primaryTicker
                            )
                        )
                    )
                )
            );
        HoldingsExcludedFiler Filer(DateOnly date, Guid holder) =>
            filers.GetValueOrDefault((date, holder)) ?? new HoldingsExcludedFiler(holder, null, 0);
        return statuses.ToDictionary(
            entry => entry.Key,
            entry =>
                entry.Value with
                {
                    ExcludedFilers = entry
                        .Value.MissingPreviousFilers.Select(id => Filer(entry.Value.ReportDate, id))
                        .Concat(
                            entry.Value.MissingCurrentFilers.Select(id =>
                                Filer(entry.Value.PreviousReportDate, id)
                            )
                        )
                        .ToList(),
                }
        );
    }

    private sealed class ExcludedPosition
    {
        public DateOnly ReportDate { get; init; }
        public Guid InstitutionalHolderId { get; init; }
        public string Name { get; init; }
        public string ListedTicker { get; init; }
        public long Shares { get; init; }
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

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
        // One read carries presence and the shares both like-for-like totals sum, so the change
        // never mixes these live rows with a lagging activity snapshot.
        var positions = await holdings
            .Where(h => ordered.Contains(h.ReportDate))
            .GroupBy(h => new
            {
                h.ReportDate,
                h.InstitutionalHolderId,
                h.ListedTicker,
            })
            .Select(g => new HoldingsPositionShares
            {
                ReportDate = g.Key.ReportDate,
                InstitutionalHolderId = g.Key.InstitutionalHolderId,
                ListedTicker = g.Key.ListedTicker,
                Shares = g.Sum(h => h.Shares),
            })
            .ToListAsync(cancellationToken);
        var primaryTicker = stock.Presentation.Listing.Ticker;
        var scopes = positions
            .Select(p => p.ListedTicker ?? primaryTicker)
            .Distinct()
            .ToDictionary(
                series => series,
                series => ShareCountSplitScope.ForListedSeries(splits, primaryTicker, series)
            );
        var shares = positions
            .GroupBy(p => (p.ReportDate, p.InstitutionalHolderId))
            .ToDictionary(
                g => g.Key,
                g =>
                    g.Sum(p =>
                        SplitAdjustment.AdjustShareCount(
                            p.Shares,
                            p.ReportDate,
                            scopes[p.ListedTicker ?? primaryTicker]
                        )
                    )
            );
        var byDate = shares
            .Keys.GroupBy(key => key.ReportDate)
            .ToDictionary(g => g.Key, g => g.Select(key => key.InstitutionalHolderId).ToHashSet());
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
        var compared = resolved.ToDictionary(
            entry => entry.Key,
            entry =>
                LikeForLike(
                    entry.Value,
                    shares,
                    byDate.GetValueOrDefault(entry.Value.ReportDate) ?? [],
                    byDate.GetValueOrDefault(entry.Value.PreviousReportDate) ?? [],
                    entry.Key == combinedReportDate
                )
        );
        return await WithNames(holdings, ordered, compared, cancellationToken);
    }

    // Sums each side over filers with an observed 13F in both quarters. The combined view
    // carries every holder yet to file at its prior position, so those positions sit in both
    // totals instead of being excluded.
    private static HoldingsComparisonStatus LikeForLike(
        HoldingsComparisonStatus status,
        IReadOnlyDictionary<(DateOnly, Guid), long> shares,
        IReadOnlySet<Guid> currentHolders,
        IReadOnlySet<Guid> previousHolders,
        bool combined
    )
    {
        if (!status.ConsecutiveQuarters)
            return status;
        long SharesAt(DateOnly date, Guid holder) => shares.GetValueOrDefault((date, holder));
        var carried = combined ? status.MissingCurrentFilers : new HashSet<Guid>();
        var lapsed = combined ? new HashSet<Guid>() : status.MissingCurrentFilers;
        var current =
            currentHolders
                .Where(id => !status.MissingPreviousFilers.Contains(id))
                .Sum(id => SharesAt(status.ReportDate, id))
            + carried.Sum(id => SharesAt(status.PreviousReportDate, id));
        var previous = previousHolders
            .Where(id => !lapsed.Contains(id))
            .Sum(id => SharesAt(status.PreviousReportDate, id));
        return status with
        {
            MissingCurrentFilers = lapsed,
            ExcludedFilers = status
                .MissingPreviousFilers.Select(id => new HoldingsExcludedFiler(
                    id,
                    null,
                    SharesAt(status.ReportDate, id)
                ))
                .Concat(
                    lapsed.Select(id => new HoldingsExcludedFiler(
                        id,
                        null,
                        SharesAt(status.PreviousReportDate, id)
                    ))
                )
                .ToList(),
            LikeForLike = (current, previous),
        };
    }

    // Names only the filers a note prints.
    private static async Task<Dictionary<DateOnly, HoldingsComparisonStatus>> WithNames(
        IQueryable<InstitutionalHolding> holdings,
        DateOnly[] dates,
        Dictionary<DateOnly, HoldingsComparisonStatus> statuses,
        CancellationToken cancellationToken
    )
    {
        var named = statuses
            .Values.SelectMany(status =>
                status.LargestExcludedFilers.Select(filer => filer.InstitutionalHolderId)
            )
            .Distinct()
            .ToArray();
        if (named.Length == 0)
            return statuses;
        var names = await holdings
            .Where(h => dates.Contains(h.ReportDate) && named.Contains(h.InstitutionalHolderId))
            .Select(h => new { h.InstitutionalHolderId, h.InstitutionalHolder.Name })
            .Distinct()
            .ToDictionaryAsync(h => h.InstitutionalHolderId, h => h.Name, cancellationToken);
        return statuses.ToDictionary(
            entry => entry.Key,
            entry =>
                entry.Value with
                {
                    ExcludedFilers = entry
                        .Value.ExcludedFilers.Select(filer =>
                            filer with
                            {
                                Name = names.GetValueOrDefault(filer.InstitutionalHolderId),
                            }
                        )
                        .ToList(),
                }
        );
    }

    private sealed class HoldingsPositionShares
    {
        public DateOnly ReportDate { get; init; }
        public Guid InstitutionalHolderId { get; init; }
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
        var resolved = pending
            .Select(p =>
                p with
                {
                    MissingPreviousFilers = p
                        .MissingPreviousFilers.Except(byDate[p.PreviousReportDate])
                        .ToHashSet(),
                    MissingCurrentFilers = p
                        .MissingCurrentFilers.Except(byDate[p.ReportDate])
                        .ToHashSet(),
                }
            )
            .ToList();
        var successions = await Successions(repository, resolved, cancellationToken);
        return resolved.ToDictionary(
            p => p.ReportDate,
            p => WithSuccessions(p, successions[p.ReportDate])
        );
    }

    // The succession groups whose successors first filed in a compared quarter that still
    // misses a filer; only such a quarter can hold a CIK change.
    private static async Task<ILookup<DateOnly, HoldingsSuccessionGroup>> Successions(
        InstitutionalHoldingRepository repository,
        IReadOnlyList<HoldingsComparisonStatus> resolved,
        CancellationToken cancellationToken
    )
    {
        var quarters = resolved
            .Where(p =>
                p.ConsecutiveQuarters
                && (p.MissingPreviousFilers.Count > 0 || p.MissingCurrentFilers.Count > 0)
            )
            .Select(p => p.ReportDate)
            .Distinct()
            .ToArray();
        if (quarters.Length == 0)
            return Array.Empty<HoldingsSuccessionGroup>().ToLookup(group => group.ReportDate);
        var pairs = await repository
            .GetSuccessionsFirstReportedOn(quarters)
            .Select(succession => new HoldingsSuccessionPair
            {
                PredecessorHolderId = succession.PredecessorHolderId,
                PredecessorName = succession.PredecessorHolder.Name,
                SuccessorHolderId = succession.SuccessorHolderId,
                SuccessorName = succession.SuccessorHolder.Name,
                SuccessorFirstReportDate = succession.SuccessorFirstReportDate,
                PredecessorGroupValue = succession.PredecessorGroupValue,
                SuccessorGroupValue = succession.SuccessorGroupValue,
            })
            .ToListAsync(cancellationToken);
        return HoldingsSuccessionGroups.Build(pairs).ToLookup(group => group.ReportDate);
    }

    // A comparable group filed on both sides: its predecessors' last 13F is the previous quarter
    // and its successors' first is this one, so none of them is missing from the comparison.
    private static HoldingsComparisonStatus WithSuccessions(
        HoldingsComparisonStatus status,
        IEnumerable<HoldingsSuccessionGroup> groups
    )
    {
        var relevant = groups
            .Where(group =>
                group.Predecessors.Keys.Any(status.MissingCurrentFilers.Contains)
                || group.Successors.Keys.Any(status.MissingPreviousFilers.Contains)
            )
            .ToList();
        if (relevant.Count == 0)
            return status;
        var comparable = relevant.Where(group => group.Comparable).ToList();
        return status with
        {
            MissingPreviousFilers = status
                .MissingPreviousFilers.Except(comparable.SelectMany(group => group.Successors.Keys))
                .ToHashSet(),
            MissingCurrentFilers = status
                .MissingCurrentFilers.Except(
                    comparable.SelectMany(group => group.Predecessors.Keys)
                )
                .ToHashSet(),
            Successions = relevant,
        };
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
            // A source-verified zero-holdings restatement is a filed 13F too, as the combined view
            // counts it, so its holder exited rather than went unobserved.
            var branch = repository
                .GetFiledHolderIdsAmong(date, holders)
                .Select(id => new HoldingsFilingPresence
                {
                    ReportDate = date,
                    InstitutionalHolderId = id,
                });
            query = query == null ? branch : query.Concat(branch);
        }
        return query;
    }
}

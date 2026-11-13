using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories.Extensions;

public static class HolderReportedBookQueries
{
    /// <summary>
    /// Each filer's book as it is reported today (<see cref="HolderReportedBook"/>), keyed by the
    /// filer's own id: three bounded reads however many filers are asked for. A filer without
    /// materialised quarters falls back to its own holdings.
    /// </summary>
    public static Task<Dictionary<Guid, HolderReportedBook>> GetReportedBooks(
        this InstitutionalHoldingRepository holdings,
        InstitutionalHolderRepository holders,
        IReadOnlyCollection<InstitutionalHolder> filers,
        DateOnly today,
        CancellationToken cancellationToken = default
    ) =>
        Read(
            holders,
            filers,
            today,
            ids => holdings.GetHolderQuarterlySnapshotsSnapshotBacked(ids, cancellationToken),
            cancellationToken
        );

    /// <summary>The same books from materialised quarters only, for a page that must never group live holdings.</summary>
    public static Task<Dictionary<Guid, HolderReportedBook>> GetReportedBooks(
        this HolderQuarterlySnapshotRepository snapshots,
        InstitutionalHolderRepository holders,
        IReadOnlyCollection<InstitutionalHolder> filers,
        DateOnly today,
        CancellationToken cancellationToken = default
    ) =>
        Read(
            holders,
            filers,
            today,
            ids =>
                snapshots
                    .GetAll()
                    .Where(quarter => ids.Contains(quarter.InstitutionalHolderId))
                    .ToListAsync(cancellationToken),
            cancellationToken
        );

    private static async Task<Dictionary<Guid, HolderReportedBook>> Read(
        InstitutionalHolderRepository holders,
        IReadOnlyCollection<InstitutionalHolder> filers,
        DateOnly today,
        Func<List<Guid>, Task<List<HolderQuarterlySnapshot>>> loadQuarters,
        CancellationToken cancellationToken
    )
    {
        var canonicalCiks = filers
            .Select(filer => CikNormalizer.Canonicalize(filer.Cik))
            .Where(cik => cik != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var topByCik = await holders
            .GetRollups(canonicalCiks)
            .Where(rollup => rollup.ReportingTopCik != null && rollup.ReportingTopCik != rollup.Cik)
            .ToDictionaryAsync(
                rollup => rollup.Cik,
                rollup => rollup.ReportingTopCik,
                cancellationToken
            );
        var reporters =
            topByCik.Count == 0
                ? []
                : await holders.GetByCiks(
                    topByCik.Values.Distinct(StringComparer.Ordinal),
                    cancellationToken
                );

        var quartersByHolder = (
            await loadQuarters(
                filers.Concat(reporters).Select(holder => holder.Id).Distinct().ToList()
            )
        ).ToLookup(quarter => quarter.InstitutionalHolderId);

        return filers
            .DistinctBy(filer => filer.Id)
            .ToDictionary(
                filer => filer.Id,
                filer =>
                {
                    var cik = CikNormalizer.Canonicalize(filer.Cik);
                    var reporter =
                        cik != null && topByCik.TryGetValue(cik, out var top)
                            ? InstitutionalHolderRepository.MatchRequestedCik(reporters, top)
                            : null;
                    return HolderReportedBook.Continue(
                        filer,
                        quartersByHolder[filer.Id],
                        reporter,
                        reporter == null ? [] : quartersByHolder[reporter.Id],
                        today
                    );
                }
            );
    }
}

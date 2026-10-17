using Equibles.CommonStocks.Data.Helpers;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Recomputes which 13F filers continue under another CIK (<see cref="HolderSuccessionRule"/>)
/// from the stored 13F rollups, their included-manager lists and 13F notices, and replaces the
/// stored set in one transaction. A predecessor that files a 13F again drops out on the next run.
/// </summary>
[Service]
public class HolderSuccessionRefreshService
{
    // Bounds each IN list sent to Postgres.
    private const int QueryBatchSize = 5_000;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<HolderSuccessionRefreshService> _logger;

    public HolderSuccessionRefreshService(
        EquiblesFinancialDbContext dbContext,
        ILogger<HolderSuccessionRefreshService> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>Returns the number of successions stored.</summary>
    public async Task<int> Run(DateOnly today, CancellationToken cancellationToken)
    {
        var latestClosedQuarter = LatestClosedQuarter(today);
        var filers = await LoadFilers(cancellationToken);
        var startQuarters = filers
            .Select(filer =>
                HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(filer.LastReportDate.AddDays(1))
            )
            .Where(quarter => quarter <= latestClosedQuarter)
            .ToHashSet();
        var predecessors = filers
            .Where(filer =>
                startQuarters.Contains(
                    HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(filer.LastReportDate.AddDays(1))
                )
            )
            .ToList();
        var successors = filers
            .Where(filer => startQuarters.Contains(filer.FirstReportDate))
            .ToList();
        var candidates = predecessors
            .Concat(successors)
            .DistinctBy(filer => filer.HolderId)
            .ToList();

        var reports = await LoadReports(predecessors, successors, cancellationToken);
        var notices = await LoadNotices(predecessors, cancellationToken);
        var successions = HolderSuccessionRule.Derive(
            candidates,
            reports,
            notices,
            latestClosedQuarter
        );

        await Replace(successions, cancellationToken);
        _logger.LogInformation(
            "Holder successions refreshed: {Count} from {Predecessors} predecessors",
            successions.Count,
            successions.Select(succession => succession.PredecessorHolderId).Distinct().Count()
        );
        return successions.Count;
    }

    // The newest quarter whose filing window has closed.
    internal static DateOnly LatestClosedQuarter(DateOnly today)
    {
        var cutoff = CombinedQuarterHelper.CompletedReportDateCutoff(today);
        var quarter = HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(cutoff);
        return quarter > cutoff ? HoldingsCorpusCoverage.PreviousQuarterEnd(quarter) : quarter;
    }

    private async Task<List<SuccessionFiler>> LoadFilers(CancellationToken cancellationToken)
    {
        var spans = await _dbContext
            .Set<InstitutionalFiling>()
            .AsNoTracking()
            .Where(filing =>
                filing.FilingType == FilingType.Form13F
                && filing.ReportDate.Month % 3 == 0
                && filing.ReportDate.AddDays(1).Day == 1
            )
            .GroupBy(filing => filing.InstitutionalHolderId)
            .Select(group => new
            {
                HolderId = group.Key,
                First = group.Min(filing => filing.ReportDate),
                Last = group.Max(filing => filing.ReportDate),
            })
            .ToListAsync(cancellationToken);
        var holders = await _dbContext
            .Set<InstitutionalHolder>()
            .AsNoTracking()
            .Select(holder => new
            {
                holder.Id,
                holder.Cik,
                holder.Form13FFileNumber,
                holder.CrdNumber,
                holder.SecFileNumber,
            })
            .ToDictionaryAsync(holder => holder.Id, cancellationToken);
        return spans
            .Where(span => holders.ContainsKey(span.HolderId))
            .Select(span =>
            {
                var holder = holders[span.HolderId];
                return new SuccessionFiler(
                    holder.Id,
                    holder.Cik,
                    holder.Form13FFileNumber,
                    holder.CrdNumber,
                    holder.SecFileNumber,
                    span.First,
                    span.Last
                );
            })
            .ToList();
    }

    // The predecessors' last and the successors' first 13F reports, with every manager each
    // included; amendments for the same quarter add their lists to the original's.
    private async Task<Dictionary<(Guid, DateOnly), SuccessionReport>> LoadReports(
        IEnumerable<SuccessionFiler> predecessors,
        IEnumerable<SuccessionFiler> successors,
        CancellationToken cancellationToken
    )
    {
        var wanted = predecessors
            .Select(filer => (filer.HolderId, filer.LastReportDate))
            .Concat(successors.Select(filer => (filer.HolderId, filer.FirstReportDate)))
            .ToHashSet();
        var filings =
            new List<(Guid HolderId, DateOnly ReportDate, string Accession, DateOnly FilingDate)>();
        foreach (var batch in wanted.Select(item => item.HolderId).Distinct().Chunk(QueryBatchSize))
        {
            var rows = await _dbContext
                .Set<InstitutionalFiling>()
                .AsNoTracking()
                .Where(filing =>
                    filing.FilingType == FilingType.Form13F
                    && batch.Contains(filing.InstitutionalHolderId)
                )
                .Select(filing => new
                {
                    filing.InstitutionalHolderId,
                    filing.ReportDate,
                    filing.AccessionNumber,
                    filing.FilingDate,
                })
                .ToListAsync(cancellationToken);
            filings.AddRange(
                rows.Where(row => wanted.Contains((row.InstitutionalHolderId, row.ReportDate)))
                    .Select(row =>
                        (
                            row.InstitutionalHolderId,
                            row.ReportDate,
                            row.AccessionNumber,
                            row.FilingDate
                        )
                    )
            );
        }

        var included = new Dictionary<string, List<ManagerReference>>(StringComparer.Ordinal);
        foreach (var batch in filings.Select(filing => filing.Accession).Chunk(QueryBatchSize))
        {
            var rows = await _dbContext
                .Set<FilingOtherManager>()
                .AsNoTracking()
                .Where(manager =>
                    manager.Direction == OtherManagerDirection.IncludedInReport
                    && batch.Contains(manager.AccessionNumber)
                )
                .Select(manager => new
                {
                    manager.AccessionNumber,
                    manager.Cik,
                    manager.Form13FFileNumber,
                    manager.CrdNumber,
                    manager.SecFileNumber,
                })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (!included.TryGetValue(row.AccessionNumber, out var list))
                    included[row.AccessionNumber] = list = [];
                list.Add(
                    new ManagerReference(
                        row.Cik,
                        row.Form13FFileNumber,
                        row.CrdNumber,
                        row.SecFileNumber
                    )
                );
            }
        }

        return filings
            .GroupBy(filing => (filing.HolderId, filing.ReportDate))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group
                        .OrderBy(filing => filing.FilingDate)
                        .ThenBy(filing => filing.Accession, StringComparer.Ordinal)
                        .ToList();
                    return new SuccessionReport(
                        ordered[0].Accession,
                        ordered
                            .SelectMany(filing =>
                                included.GetValueOrDefault(filing.Accession) ?? []
                            )
                            .ToList()
                    );
                }
            );
    }

    private async Task<List<SuccessionNotice>> LoadNotices(
        IEnumerable<SuccessionFiler> predecessors,
        CancellationToken cancellationToken
    )
    {
        var notices = new List<SuccessionNotice>();
        var ciks = predecessors
            .Select(filer => CikNormalizer.Canonicalize(filer.Cik))
            .Where(cik => cik != null)
            .Distinct()
            .ToList();
        foreach (var batch in ciks.Chunk(QueryBatchSize))
        {
            var rows = await _dbContext
                .Set<Form13FNotice>()
                .AsNoTracking()
                .Where(notice => batch.Contains(notice.FilerCik))
                .Select(notice => new
                {
                    notice.FilerCik,
                    notice.AccessionNumber,
                    notice.ReportDate,
                    Reporters = notice
                        .Reporters.Select(reporter => new
                        {
                            reporter.Cik,
                            reporter.Form13FFileNumber,
                            reporter.CrdNumber,
                            reporter.SecFileNumber,
                        })
                        .ToList(),
                })
                .ToListAsync(cancellationToken);
            notices.AddRange(
                rows.Select(row => new SuccessionNotice(
                    row.FilerCik,
                    row.AccessionNumber,
                    row.ReportDate,
                    row.Reporters.Select(reporter => new ManagerReference(
                            reporter.Cik,
                            reporter.Form13FFileNumber,
                            reporter.CrdNumber,
                            reporter.SecFileNumber
                        ))
                        .ToList()
                ))
            );
        }

        return notices;
    }

    private async Task Replace(
        List<HolderSuccession> successions,
        CancellationToken cancellationToken
    )
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            await _dbContext.Set<HolderSuccession>().ExecuteDeleteAsync(cancellationToken);
            _dbContext.AddRange(successions);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        _dbContext.ChangeTracker.Clear();
    }
}

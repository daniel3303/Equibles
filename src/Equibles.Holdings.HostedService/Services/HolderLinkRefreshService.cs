using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Links;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Recomputes holder parent links (<see cref="HolderLinkRule"/>) from the last
/// <see cref="EvidenceQuarters"/> closed quarters' 13F other-manager lists and notices, any
/// registered <see cref="IHolderLinkEvidenceSource"/>, the stored successions and current N-CEN
/// advisers, and replaces the evidence, rollups and fund-series links in one transaction.
/// </summary>
[Service]
public class HolderLinkRefreshService
{
    /// <summary>How many closed quarters of 13F evidence the links read.</summary>
    internal const int EvidenceQuarters = 8;

    // Bounds each insert batch, and the change tracker, while the replace runs.
    private const int WriteBatchSize = 5_000;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly IEnumerable<IHolderLinkEvidenceSource> _sources;
    private readonly ILogger<HolderLinkRefreshService> _logger;

    public HolderLinkRefreshService(
        EquiblesFinancialDbContext dbContext,
        IEnumerable<IHolderLinkEvidenceSource> sources,
        ILogger<HolderLinkRefreshService> logger
    )
    {
        _dbContext = dbContext;
        _sources = sources;
        _logger = logger;
    }

    /// <summary>Returns the number of evidence rows stored.</summary>
    public async Task<int> Run(DateOnly today, CancellationToken cancellationToken)
    {
        var quarters = Quarters(today);
        var holders = await LoadHolders(cancellationToken);
        var newest13FReports = await _dbContext
            .Set<InstitutionalFiling>()
            .AsNoTracking()
            .Where(filing =>
                filing.FilingType == FilingType.Form13F && quarters.Contains(filing.ReportDate)
            )
            .GroupBy(filing => filing.InstitutionalHolderId)
            .Select(group => new
            {
                HolderId = group.Key,
                Newest = group.Max(filing => filing.ReportDate),
            })
            .ToDictionaryAsync(row => row.HolderId, row => row.Newest, cancellationToken);
        var managers = new List<LinkManagerRow>();
        var notices = new List<LinkNotice>();
        foreach (var quarter in quarters)
        {
            managers.AddRange(await LoadManagers(quarter, cancellationToken));
            notices.AddRange(await LoadNotices(quarter, cancellationToken));
        }
        var successions = (
            await _dbContext
                .Set<HolderSuccession>()
                .AsNoTracking()
                .Select(succession => new
                {
                    succession.PredecessorHolderId,
                    succession.SuccessorHolderId,
                })
                .ToListAsync(cancellationToken)
        )
            .Select(pair => (pair.PredecessorHolderId, pair.SuccessorHolderId))
            .ToList();
        var sourced = new List<HolderLinkEvidence>();
        foreach (var source in _sources)
            sourced.AddRange(await source.Collect(today, cancellationToken));
        var seriesAdvisers = await AdvisedSeriesLoader.LoadSeriesAdvisers(
            _dbContext,
            today,
            cancellationToken
        );

        var links = HolderLinkRule.Derive(
            holders,
            newest13FReports,
            managers,
            notices,
            successions,
            sourced,
            seriesAdvisers
        );

        await Replace(links, cancellationToken);
        _logger.LogInformation(
            "Holder links refreshed: {Evidence} evidence rows, {Parented} filers under a parent ({Conflicts} conflicting), {FundLinks} fund series linked",
            links.Evidence.Count,
            links.Rollups.Count(rollup => rollup.UltimateCik != rollup.Cik),
            links.Rollups.Count(rollup => rollup.HasConflictingEvidence),
            links.FundSeriesLinks.Count
        );
        return links.Evidence.Count;
    }

    // The newest closed quarter and the ones before it: notices arrive only with the quarterly
    // data sets, so an open quarter's included edges would lack the child's confirming notice.
    internal static List<DateOnly> Quarters(DateOnly today)
    {
        var quarter = HolderSuccessionRefreshService.LatestClosedQuarter(today);
        var quarters = new List<DateOnly>(EvidenceQuarters);
        for (var index = 0; index < EvidenceQuarters; index++)
        {
            quarters.Add(quarter);
            quarter = HoldingsCorpusCoverage.PreviousQuarterEnd(quarter);
        }
        return quarters;
    }

    private async Task<List<LinkHolder>> LoadHolders(CancellationToken cancellationToken)
    {
        var adviserCrds = await _dbContext
            .Set<InstitutionalHolderProfile>()
            .AsNoTracking()
            .Where(profile => profile.AdviserCrd != null)
            .Select(profile => new { profile.InstitutionalHolderId, profile.AdviserCrd })
            .ToDictionaryAsync(
                profile => profile.InstitutionalHolderId,
                profile => profile.AdviserCrd,
                cancellationToken
            );
        return (
            await _dbContext
                .Set<InstitutionalHolder>()
                .AsNoTracking()
                .Select(holder => new
                {
                    holder.Id,
                    holder.Cik,
                    holder.Form13FFileNumber,
                    holder.CrdNumber,
                })
                .ToListAsync(cancellationToken)
        ).Select(holder => new LinkHolder(holder.Id, holder.Cik, holder.Form13FFileNumber, holder.CrdNumber, adviserCrds.GetValueOrDefault(holder.Id))).ToList();
    }

    // One quarter's 13F other-manager rows, both lists, with the filing each comes from; an
    // amendment's rows read with the original's.
    private async Task<List<LinkManagerRow>> LoadManagers(
        DateOnly quarter,
        CancellationToken cancellationToken
    ) => (
            await (
                from manager in _dbContext.Set<FilingOtherManager>().AsNoTracking()
                join filing in _dbContext.Set<InstitutionalFiling>().AsNoTracking()
                    on manager.AccessionNumber equals filing.AccessionNumber
                where filing.FilingType == FilingType.Form13F && filing.ReportDate == quarter
                select new
                {
                    manager.AccessionNumber,
                    filing.InstitutionalHolderId,
                    filing.FilingDate,
                    manager.Direction,
                    manager.Cik,
                    manager.Form13FFileNumber,
                    manager.CrdNumber,
                    manager.SecFileNumber,
                }
            ).ToListAsync(cancellationToken)
        ).Select(row => new LinkManagerRow(row.AccessionNumber, row.InstitutionalHolderId, quarter, row.FilingDate, row.Direction, new ManagerReference(row.Cik, row.Form13FFileNumber, row.CrdNumber, row.SecFileNumber))).ToList();

    private async Task<List<LinkNotice>> LoadNotices(
        DateOnly quarter,
        CancellationToken cancellationToken
    ) => (
            await _dbContext
                .Set<Form13FNotice>()
                .AsNoTracking()
                .Where(notice => notice.ReportDate == quarter)
                .Select(notice => new
                {
                    notice.AccessionNumber,
                    notice.FilerCik,
                    notice.Form13FFileNumber,
                    notice.CrdNumber,
                    notice.FilingDate,
                    Reporters = notice
                        .Reporters.OrderBy(reporter => reporter.Ordinal)
                        .Select(reporter => new
                        {
                            reporter.Cik,
                            reporter.Form13FFileNumber,
                            reporter.CrdNumber,
                            reporter.SecFileNumber,
                        })
                        .ToList(),
                })
                .ToListAsync(cancellationToken)
        ).Select(notice => new LinkNotice(notice.AccessionNumber, notice.FilerCik, notice.Form13FFileNumber, notice.CrdNumber, quarter, notice.FilingDate, notice.Reporters.Select(reporter => new ManagerReference(reporter.Cik, reporter.Form13FFileNumber, reporter.CrdNumber, reporter.SecFileNumber)).ToList())).ToList();

    private async Task Replace(HolderLinkSet links, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            await _dbContext.Set<HolderLinkEvidence>().ExecuteDeleteAsync(cancellationToken);
            await _dbContext.Set<HolderRollup>().ExecuteDeleteAsync(cancellationToken);
            await _dbContext.Set<FundSeriesHolderLink>().ExecuteDeleteAsync(cancellationToken);
            await Insert(links.Evidence, cancellationToken);
            await Insert(links.Rollups, cancellationToken);
            await Insert(links.FundSeriesLinks, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        _dbContext.ChangeTracker.Clear();
    }

    private async Task Insert<T>(IEnumerable<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        foreach (var batch in rows.Chunk(WriteBatchSize))
        {
            _dbContext.AddRange(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }
    }
}

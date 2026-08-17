using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Ingests congressional annual financial disclosures from both chambers and
/// rolls each electronically-filed report into a
/// <see cref="CongressionalAnnualDisclosure"/> with its net-worth band
/// (Σ asset minimums − Σ liability maximums through Σ asset maximums −
/// Σ liability minimums). Idempotent: a report already stored under the same
/// source id is skipped, and a later-filed report (amendment) replaces the
/// member-year row in place.
/// </summary>
[Service]
public class CongressionalAnnualDisclosureSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CongressionalAnnualDisclosureSyncService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;
    private readonly CongressionalFilingLedger _filingLedger;

    // Electronic filing of congressional disclosures dates to the STOCK Act.
    private const int EarliestCoverageYear = 2012;

    // Without an explicit MinSyncDate, re-sync the last few coverage years:
    // annual reports for year Y arrive throughout Y+1 (extensions push many to
    // late Y+1) and amendments trail in afterwards.
    private const int DefaultCoverageYearsBack = 2;

    // Raise this whenever the schedule parsers start extracting a field their
    // stored rows do not carry; already-ingested filings then re-download and
    // re-parse so the new field is populated for history too, instead of only
    // appearing on filings submitted from now on.
    //   1 — asset type, income type and the income bracket (GH-7347).
    private const int AnnualParserVersion = 1;

    // Filings re-parsed per 24h cycle after a version bump. The clients fetch
    // at 5 requests/second, so this is a runaway guard rather than a politeness
    // limit: a normal backfill of the whole archive still drains in a few days.
    private const int ReprocessPerCycleLimit = 1_000;

    public CongressionalAnnualDisclosureSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CongressionalAnnualDisclosureSyncService> logger,
        ErrorReporter errorReporter,
        CongressionalFilingLedger filingLedger
    )
    {
        _scopeFactory = scopeFactory;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _filingLedger = filingLedger;
    }

    public async Task SyncAll(CancellationToken ct)
    {
        var currentYear = DateTime.UtcNow.Year;
        var fromYear = Math.Max(
            _workerOptions.MinSyncDate?.Year ?? currentYear - DefaultCoverageYearsBack,
            EarliestCoverageYear
        );

        _logger.LogInformation(
            "Starting congressional annual disclosure sync for coverage years {From}-{To}",
            fromYear,
            currentYear
        );

        var houseProcessedIds = await _filingLedger.GetProcessedSourceIds(
            CongressionalFilingKind.HouseAnnualReport,
            ct,
            AnnualParserVersion,
            ReprocessPerCycleLimit
        );
        var senateProcessedIds = await _filingLedger.GetProcessedSourceIds(
            CongressionalFilingKind.SenateAnnualReport,
            ct,
            AnnualParserVersion,
            ReprocessPerCycleLimit
        );

        var houseResult = await FetchReports(
            "House",
            "CongressAnnual.SyncHouse",
            async sp =>
            {
                var client = sp.GetRequiredService<HouseAnnualReportClient>();
                var fetched = new AnnualReportFetchResult();
                for (var year = fromYear; year <= currentYear; year++)
                {
                    var yearResult = await client.GetAnnualReports(year, houseProcessedIds, ct);
                    fetched.Reports.AddRange(yearResult.Reports);
                    fetched.ProcessedFilings.AddRange(yearResult.ProcessedFilings);
                }
                return fetched;
            },
            ct
        );

        // Senate reports are searched by submitted date; reports covering year
        // Y are filed from Y+1 onwards, and any late amendment of an older
        // year inside the window still updates that year.
        var senateResult = await FetchReports(
            "Senate",
            "CongressAnnual.SyncSenate",
            sp =>
                sp.GetRequiredService<SenateAnnualReportClient>()
                    .GetAnnualReports(
                        new DateOnly(fromYear + 1, 1, 1),
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        senateProcessedIds,
                        ct
                    ),
            ct
        );

        var reports = houseResult.Reports.Concat(senateResult.Reports).ToList();

        IReadOnlySet<string> unpersistedReportIds = new HashSet<string>();
        if (reports.Count == 0)
            _logger.LogInformation("No congressional annual disclosure reports found");
        else
            unpersistedReportIds = await ProcessReports(reports, ct);

        // Only after the reports are committed: a failed persist above throws
        // before this point, so unrecorded filings re-fetch next cycle instead
        // of being lost. Reports that were parsed but not stored (the
        // member-not-found guard) stay unrecorded so they keep retrying.
        await _filingLedger.RecordProcessed(
            CongressionalFilingKind.HouseAnnualReport,
            houseResult
                .ProcessedFilings.Where(f => !unpersistedReportIds.Contains(f.SourceId))
                .ToList(),
            ct,
            AnnualParserVersion
        );
        await _filingLedger.RecordProcessed(
            CongressionalFilingKind.SenateAnnualReport,
            senateResult
                .ProcessedFilings.Where(f => !unpersistedReportIds.Contains(f.SourceId))
                .ToList(),
            ct,
            AnnualParserVersion
        );
    }

    private async Task<AnnualReportFetchResult> FetchReports(
        string sourceLabel,
        string errorContext,
        Func<IServiceProvider, Task<AnnualReportFetchResult>> fetch,
        CancellationToken ct
    )
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            return await fetch(scope.ServiceProvider);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {Source} annual disclosure data", sourceLabel);
            await _errorReporter.Report(ErrorSource.CongressScraper, errorContext, ex);
            return new AnnualReportFetchResult();
        }
    }

    // Returns the source report ids that were parsed but not stored (the
    // member-not-found guard) so the caller leaves their filings unrecorded.
    private async Task<IReadOnlySet<string>> ProcessReports(
        List<AnnualDisclosureReport> reports,
        CancellationToken ct
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var memberRepository = scope.ServiceProvider.GetRequiredService<CongressMemberRepository>();
        var disclosureRepository =
            scope.ServiceProvider.GetRequiredService<CongressionalAnnualDisclosureRepository>();

        var latest = SelectLatestReports(reports);
        var members = await UpsertCongressMembers(latest, dbContext, memberRepository, ct);

        var unpersistedReportIds = new HashSet<string>();
        int added = 0,
            replaced = 0,
            unchanged = 0;

        foreach (var report in latest)
        {
            ct.ThrowIfCancellationRequested();

            var memberName = DisclosureParsingHelper.NormalizeMemberName(report.MemberName);
            if (!members.TryGetValue(memberName, out var member))
            {
                _logger.LogWarning("Congress member not found after upsert: {Name}", memberName);
                if (report.ReportId != null)
                    unpersistedReportIds.Add(report.ReportId);
                continue;
            }

            var existing = await disclosureRepository
                .GetAll()
                .FirstOrDefaultAsync(
                    d => d.CongressMemberId == member.Id && d.Year == report.Year,
                    ct
                );

            if (existing == null)
            {
                dbContext.Add(BuildDisclosure(report, member));
                added++;
            }
            else if (ShouldReplace(existing, report))
            {
                ReplaceDisclosure(existing, report, dbContext);
                replaced++;
            }
            else
            {
                unchanged++;
            }
        }

        await dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Congressional annual disclosure sync stored {Added} new and {Replaced} amended reports ({Unchanged} unchanged)",
            added,
            replaced,
            unchanged
        );

        return unpersistedReportIds;
    }

    private async Task<Dictionary<string, CongressMember>> UpsertCongressMembers(
        List<AnnualDisclosureReport> reports,
        EquiblesFinancialDbContext dbContext,
        CongressMemberRepository memberRepository,
        CancellationToken ct
    )
    {
        // Canonicalise the name so cosmetic disclosure variants resolve to one
        // CongressMember record (GH-3374) — the same identity key the trade
        // sync uses, so the two pipelines converge on the same member.
        var distinctMembers = reports
            .GroupBy(r => DisclosureParsingHelper.NormalizeMemberName(r.MemberName))
            .Select(g => new CongressMember
            {
                Name = g.Key,
                Position = g.First().Position,
                StateDistrict = SelectStateDistrict(g),
            })
            .ToList();

        await dbContext
            .Set<CongressMember>()
            .UpsertRange(distinctMembers)
            .On(m => new { m.Name })
            .WhenMatched(
                (existing, incoming) =>
                    new CongressMember
                    {
                        Position = incoming.Position,
                        // Only the House index publishes a seat, so a member's
                        // Senate reports carry none — coalescing keeps a
                        // recorded seat instead of blanking it on every cycle.
                        StateDistrict = incoming.StateDistrict ?? existing.StateDistrict,
                    }
            )
            .RunAsync(ct);

        var memberNames = distinctMembers.Select(m => m.Name).ToList();
        return await memberRepository
            .GetAll()
            .Where(m => memberNames.Contains(m.Name))
            .ToDictionaryAsync(m => m.Name, ct);
    }

    /// <summary>
    /// The seat to record for a member this cycle. Redistricting moves a member
    /// between districts, so the most recently filed report that states one
    /// wins; reports that state none (every Senate filing) are ignored rather
    /// than treated as "no seat".
    /// </summary>
    internal static string SelectStateDistrict(IEnumerable<AnnualDisclosureReport> reports) =>
        reports
            .Where(r => !string.IsNullOrWhiteSpace(r.StateDistrict))
            .OrderBy(r => r.FiledDate)
            .LastOrDefault()
            ?.StateDistrict.Trim();

    /// <summary>
    /// One report per (member, position, year): the latest filed wins, and an
    /// amendment beats the original on a same-day refiling.
    /// </summary>
    internal static List<AnnualDisclosureReport> SelectLatestReports(
        IEnumerable<AnnualDisclosureReport> reports
    ) =>
        reports
            .GroupBy(r => (r.MemberName, r.Position, r.Year))
            .Select(g => g.OrderBy(r => r.FiledDate).ThenBy(r => r.IsAmendment).Last())
            .ToList();

    /// <summary>
    /// Amendments replace the stored report in place: a different source
    /// report filed on a later date (or an amendment refiled the same day)
    /// wins; re-encountering the stored report id is a no-op.
    /// </summary>
    internal static bool ShouldReplace(
        CongressionalAnnualDisclosure existing,
        AnnualDisclosureReport incoming
    )
    {
        if (incoming.ReportId == existing.ReportId)
            return false;
        if (incoming.FiledDate != existing.FiledDate)
            return incoming.FiledDate > existing.FiledDate;
        return incoming.IsAmendment;
    }

    /// <summary>
    /// The net-worth band: minimum = Σ asset range minimums − Σ liability
    /// range maximums; maximum = Σ asset range maximums − Σ liability range
    /// minimums. Never a point estimate.
    /// </summary>
    internal static (long Minimum, long Maximum) ComputeNetWorthBand(
        IReadOnlyList<AnnualDisclosureLineItem> lines
    )
    {
        long assetMinimums = 0,
            assetMaximums = 0,
            liabilityMinimums = 0,
            liabilityMaximums = 0;

        foreach (var line in lines)
        {
            if (line.Kind == CongressionalDisclosureLineKind.Asset)
            {
                assetMinimums += line.RangeMinimum;
                assetMaximums += line.RangeMaximum;
            }
            else
            {
                liabilityMinimums += line.RangeMinimum;
                liabilityMaximums += line.RangeMaximum;
            }
        }

        return (assetMinimums - liabilityMaximums, assetMaximums - liabilityMinimums);
    }

    private static CongressionalAnnualDisclosure BuildDisclosure(
        AnnualDisclosureReport report,
        CongressMember member
    )
    {
        var (minimum, maximum) = ComputeNetWorthBand(report.Lines);
        return new CongressionalAnnualDisclosure
        {
            CongressMemberId = member.Id,
            Year = report.Year,
            FiledDate = report.FiledDate,
            ReportId = report.ReportId,
            NetWorthMinimum = minimum,
            NetWorthMaximum = maximum,
            Lines = report.Lines.Select(BuildLine).ToList(),
        };
    }

    private static void ReplaceDisclosure(
        CongressionalAnnualDisclosure existing,
        AnnualDisclosureReport report,
        EquiblesFinancialDbContext dbContext
    )
    {
        var (minimum, maximum) = ComputeNetWorthBand(report.Lines);
        existing.FiledDate = report.FiledDate;
        existing.ReportId = report.ReportId;
        existing.NetWorthMinimum = minimum;
        existing.NetWorthMaximum = maximum;

        dbContext.RemoveRange(existing.Lines);
        existing.Lines = report
            .Lines.Select(l =>
            {
                var line = BuildLine(l);
                line.CongressionalAnnualDisclosureId = existing.Id;
                return line;
            })
            .ToList();
    }

    private static CongressionalDisclosureLine BuildLine(AnnualDisclosureLineItem item) =>
        new()
        {
            Kind = item.Kind,
            Description = item.Description,
            RangeMinimum = item.RangeMinimum,
            RangeMaximum = item.RangeMaximum,
            AssetType = item.AssetType,
            IncomeType = item.IncomeType,
            IncomeMinimum = item.IncomeMinimum,
            IncomeMaximum = item.IncomeMaximum,
        };
}

using System.IO.Compression;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>Reconciles source positions independently of successful import markers.</summary>
[Service]
public class HoldingsArchiveCoverageService(
    HoldingsImportService importer,
    HoldingsDataSetClient dataSets,
    ProcessedDataSetRepository processed,
    InstitutionalHolderRepository holders,
    InstitutionalHoldingRepository holdings,
    HoldingsImportFailureRepository failures,
    HoldingsRealtimeReplaySignal signal,
    ILogger<HoldingsArchiveCoverageService> logger
)
{
    public async Task AuditLatest(DateOnly minReportDate, CancellationToken cancellationToken)
    {
        var names = await processed
            .GetAll()
            .Select(row => row.FileName)
            .ToListAsync(cancellationToken);
        var latest = names
            .Select(name => new
            {
                Name = name,
                End = Holdings13FRealtimeWorker.ParseDataSetEndDate(name),
            })
            .Where(row => row.End != null)
            .OrderByDescending(row => row.End)
            .FirstOrDefault();
        if (latest == null || latest.End < minReportDate)
            return;
        await processed.QueueCoverageAudit(cancellationToken);
        using var archive = await dataSets.DownloadDataSet(latest.Name, cancellationToken);
        await Audit(archive, minReportDate, cancellationToken);
        await processed
            .GetByFileName(ProcessedDataSet.CoverageAuditPendingFileName)
            .ExecuteDeleteAsync(cancellationToken);
    }

    internal async Task Audit(
        ZipArchive archive,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        var context = await importer.ReadCoverageContext(archive, minReportDate, cancellationToken);
        var dated = context
            .Submissions.Values.Where(row =>
                TryParseDateOnly(row.PeriodOfReport, out var date)
                && date <= DateOnly.FromDateTime(DateTime.UtcNow)
                && date.Month % 3 == 0
                && date.Day == DateTime.DaysInMonth(date.Year, date.Month)
            )
            .ToList();
        if (dated.Count == 0)
            throw new InvalidDataException(
                "The source archive has no completed 13F reporting quarter to audit."
            );
        var reportDate = dated.Max(row =>
        {
            TryParseDateOnly(row.PeriodOfReport, out var date);
            return date;
        });
        var selected = dated
            .Where(row =>
            {
                TryParseDateOnly(row.PeriodOfReport, out var date);
                return date == reportDate;
            })
            .ToDictionary(row => row.AccessionNumber);
        var latestDates = selected
            .Values.GroupBy(row => row.Cik)
            .ToDictionary(
                group => group.Key,
                group =>
                    group.Max(row =>
                    {
                        TryParseDateOnly(row.FilingDate, out var date);
                        return date;
                    })
            );
        var allCiks = selected.Values.Select(row => row.Cik).Distinct().ToList();
        var holderIds = (await holders.GetByCiks(allCiks, cancellationToken)).ToDictionary(
            row => row.Cik,
            row => row.Id
        );
        var pending = await failures
            .GetAll()
            .Where(row => row.ResolvedAt == null)
            .Select(row => row.AccessionNumber)
            .ToHashSetAsync(cancellationToken);
        var expected =
            new HashSet<(Guid Issuer, string Cusip, ShareType Shares, OptionType? Option)>();
        string accession = null;
        var checkedFilings = 0;
        var missingFilings = 0;
        var laterFilings = 0;

        async Task Check()
        {
            if (
                accession == null
                || expected.Count == 0
                || !selected.TryGetValue(accession, out var submission)
            )
                return;
            holderIds.TryGetValue(submission.Cik, out var holderId);
            var stored = await holdings
                .GetAll()
                .Where(row =>
                    row.InstitutionalHolderId == holderId
                    && row.ReportDate == reportDate
                    && row.FilingType == FilingType.Form13F
                )
                .Select(row => new
                {
                    row.EquityIssuerId,
                    row.Cusip,
                    row.ShareType,
                    row.OptionType,
                    row.FilingDate,
                })
                .ToListAsync(cancellationToken);
            // A later restatement may legitimately remove a source position. Its own import
            // and recovery record own that newer book; never reintroduce an old position here.
            var later =
                stored.Any(row => row.FilingDate > latestDates[submission.Cik])
                || await holdings
                    .GetFilingsByHolder(new InstitutionalHolder { Id = holderId }, reportDate)
                    .AnyAsync(
                        row =>
                            row.FilingType == FilingType.Form13F
                            && row.FilingDate > latestDates[submission.Cik],
                        cancellationToken
                    );
            if (later)
            {
                laterFilings++;
                return;
            }
            checkedFilings++;
            var actual = stored
                .Select(row =>
                    (
                        row.EquityIssuerId,
                        row.Cusip?.ToUpperInvariant(),
                        row.ShareType,
                        row.OptionType
                    )
                )
                .ToHashSet();
            if (expected.IsSubsetOf(actual))
                return;
            missingFilings++;
            if (!pending.Add(accession))
                return;
            if (!TryParseDateOnly(submission.FilingDate, out var filed))
                throw new InvalidDataException(
                    "A source filing with missing positions has no filing date."
                );
            await failures.Record(
                accession,
                submission.Cik,
                filed,
                reportDate,
                HoldingsImportFailureReason.MissingSourcePosition,
                cancellationToken
            );
        }

        var infoTable = FindEntry(archive, "INFOTABLE.tsv");
        await foreach (var row in context.TsvParser.ParseEntry(infoTable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = GetValue(row, "ACCESSION_NUMBER");
            if (next != accession)
            {
                await Check();
                expected.Clear();
                accession = next;
            }
            if (!selected.ContainsKey(next))
                continue;
            var cusip = GetValue(row, "CUSIP");
            if (!context.CusipMapping.TryGetValue(cusip, out var target))
                continue;
            expected.Add(
                (
                    target.CommonStockId,
                    cusip.ToUpperInvariant(),
                    ParseShareType(GetValue(row, "SSHPRNAMTTYPE")),
                    ParseOptionType(GetValue(row, "PUTCALL"))
                )
            );
        }
        await Check();
        if (missingFilings > 0)
            signal.RequestReplay();
        logger.LogInformation(
            "13F source coverage for {ReportDate}: checked {Checked} filings, {Missing} with missing tracked positions queued; {Later} have newer filings",
            reportDate,
            checkedFilings,
            missingFilings,
            laterFilings
        );
    }
}

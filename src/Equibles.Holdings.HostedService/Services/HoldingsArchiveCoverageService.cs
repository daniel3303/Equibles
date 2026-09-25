using System.Globalization;
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
        var additions = await ReadAdditionKeys(context, selected, cancellationToken);
        var expected = new Dictionary<PositionKey, HashSet<string>>();
        var expectedShares = new Dictionary<PositionKey, decimal?>();
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
                    row.Id,
                    row.EquityIssuerId,
                    row.Cusip,
                    row.ShareType,
                    row.OptionType,
                    row.FilingDate,
                    row.Shares,
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
            var actual = stored.ToLookup(row =>
                (row.EquityIssuerId, row.Cusip?.ToUpperInvariant(), row.ShareType, row.OptionType)
            );
            var matched = new HashSet<Guid>();
            var complete = true;
            foreach (var (key, cusips) in expected)
            {
                // NEW HOLDINGS replaces overlapping persistence keys, but leaves the rest of
                // the base book intact. Do not demand the overwritten CUSIP from an older key.
                if (
                    additions.TryGetValue((submission.Cik, key), out var addition)
                    && HoldingsImportService.CompareByFilingDateThenAccession(addition, submission)
                        > 0
                )
                    continue;
                // The importer merges source CUSIPs at this exact listing/share/option key.
                // A stored representative must belong to that source group. Its display label
                // may have been retained by replay; multiple observations stay a refusal.
                var matches = cusips
                    .SelectMany(cusip => actual[(key.Issuer, cusip, key.Shares, key.Option)])
                    .ToList();
                if (matches.Count != 1 || !matched.Add(matches[0].Id))
                    complete = false;
                else if (cusips.Count > 1 && matches[0].Shares != expectedShares[key])
                    // Presence of just one source leg does not prove that the importer combined
                    // the group. Unknown or altered counts remain pending for investigation.
                    complete = false;
            }
            if (complete)
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
                expectedShares.Clear();
                accession = next;
            }
            if (!selected.ContainsKey(next))
                continue;
            var cusip = GetValue(row, "CUSIP");
            if (!context.CusipMapping.TryGetValue(cusip, out var target))
                continue;
            var key = Key(target, row);
            if (!expected.TryGetValue(key, out var cusips))
            {
                expected[key] = cusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                expectedShares[key] = 0;
            }
            cusips.Add(cusip.ToUpperInvariant());
            expectedShares[key] = long.TryParse(
                GetValue(row, "SSHPRNAMT"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var shares
            )
                ? expectedShares[key] + shares
                : null;
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

    private readonly record struct PositionKey(
        Guid Issuer,
        string ListedTicker,
        ShareType Shares,
        OptionType? Option
    );

    private static PositionKey Key(CusipTarget target, Dictionary<string, string> row) =>
        new(
            target.CommonStockId,
            target.ListedTicker,
            ParseShareType(GetValue(row, "SSHPRNAMTTYPE")),
            ParseOptionType(GetValue(row, "PUTCALL"))
        );

    private static async Task<
        Dictionary<(string Cik, PositionKey Position), SubmissionRow>
    > ReadAdditionKeys(
        ImportContext context,
        Dictionary<string, SubmissionRow> selected,
        CancellationToken cancellationToken
    )
    {
        var additions = selected
            .Where(pair => HoldingsImportService.IsNewHoldingsAmendment(pair.Key, context))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var latest = new Dictionary<(string Cik, PositionKey Position), SubmissionRow>();
        if (additions.Count == 0)
            return latest;
        // Retain only additive-amendment keys, rather than buffering the whole quarterly corpus.
        await foreach (
            var row in context.TsvParser.ParseEntry(FindEntry(context.Archive, "INFOTABLE.tsv"))
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !additions.TryGetValue(GetValue(row, "ACCESSION_NUMBER"), out var submission)
                || !context.CusipMapping.TryGetValue(GetValue(row, "CUSIP"), out var target)
            )
                continue;
            var key = (submission.Cik, Key(target, row));
            if (
                !latest.TryGetValue(key, out var previous)
                || HoldingsImportService.CompareByFilingDateThenAccession(submission, previous) > 0
            )
                latest[key] = submission;
        }
        return latest;
    }
}

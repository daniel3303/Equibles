using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.Core.AutoWiring;
using Equibles.Core.Extensions;
using Equibles.Core.Identity;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.HostedService.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Imports SEC's quarterly Form N-CEN data sets, oldest quarter first: every registered fund's
/// annual report with its fund types, advisers (with CRD numbers) and tickers. A quarter is
/// replaced whole when SEC re-posts it or the parser version rises, and a fund restated by a later
/// amendment for the same period is marked superseded.
/// </summary>
[Service]
public class NCenDataSetImportService
{
    // Raise to re-import every stored quarter after a parser change.
    internal const int ParserVersion = 1;

    private const int InsertBatchSize = 500;

    internal static readonly TimeSpan FailureRetryDelay = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<NCenDataSetImportService> _logger;

    public NCenDataSetImportService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ErrorReporter errorReporter,
        ILogger<NCenDataSetImportService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _errorReporter = errorReporter;
        _logger = logger;
    }

    /// <summary>
    /// Imports up to <paramref name="maxDataSets"/> due quarters and returns true when more remain.
    /// A quarter that fails is recorded and waits <see cref="FailureRetryDelay"/>, so it never
    /// blocks later quarters.
    /// </summary>
    public async Task<bool> ImportPending(int maxDataSets, CancellationToken cancellationToken)
    {
        string html;
        await using (
            var catalogStream = await _secEdgarClient.DownloadStream(NCenDataSetCatalog.PageUrl)
        )
        using (var reader = new StreamReader(catalogStream, Encoding.UTF8))
            html = await reader.ReadToEndAsync(cancellationToken);
        var catalog = NCenDataSetCatalog.Parse(html);

        Dictionary<(int, int), NCenDataSetImport> ledger;
        using (var scope = _scopeFactory.CreateScope())
        {
            ledger = await scope
                .ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>()
                .Set<NCenDataSetImport>()
                .AsNoTracking()
                .ToDictionaryAsync(row => (row.Year, row.Quarter), cancellationToken);
        }

        var retryCutoff = DateTime.UtcNow - FailureRetryDelay;
        var due = catalog
            .Where(link =>
            {
                if (!ledger.TryGetValue((link.Year, link.Quarter), out var row))
                    return true;
                if (row.FileName == link.FileName && row.ParserVersion >= ParserVersion)
                    return false;
                return row.LastFailureAt == null || row.LastFailureAt < retryCutoff;
            })
            .ToList();

        foreach (var link in due.Take(maxDataSets))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stored = await ImportDataSet(link, cancellationToken);
                _logger.LogInformation(
                    "N-CEN data set {Year} Q{Quarter} ({File}): stored {Reports} reports",
                    link.Year,
                    link.Quarter,
                    link.FileName,
                    stored
                );
            }
            catch (Exception exception)
                when (exception is InvalidDataException
                    || exception
                        is HttpRequestException
                        {
                            StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone,
                        }
                )
            {
                _logger.LogError(
                    exception,
                    "N-CEN data set {Year} Q{Quarter} is unreadable or missing; retrying after {Delay}",
                    link.Year,
                    link.Quarter,
                    FailureRetryDelay
                );
                await RecordFailure(link, exception.Message, cancellationToken);
                await _errorReporter.Report(
                    ErrorSource.NCenDataSets,
                    "NCenDataSet.Unreadable",
                    $"N-CEN data set {link.FileName}: {exception.Message}",
                    null
                );
            }
        }

        return due.Count > maxDataSets;
    }

    private async Task RecordFailure(
        NCenDataSetLink link,
        string message,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var row = await dbContext
            .Set<NCenDataSetImport>()
            .SingleOrDefaultAsync(
                row => row.Year == link.Year && row.Quarter == link.Quarter,
                cancellationToken
            );
        if (row == null)
        {
            row = new NCenDataSetImport { Year = link.Year, Quarter = link.Quarter };
            dbContext.Add(row);
        }
        row.LastFailureAt = DateTime.UtcNow;
        row.LastFailure = message.TruncateToFit(1024);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ImportDataSet(NCenDataSetLink link, CancellationToken cancellationToken)
    {
        List<NCenRegistrantReportData> parsed;
        await using (var zipStream = await _secEdgarClient.DownloadStream(link.Url))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            parsed = NCenDataSetParser.Parse(archive);

        var reports = parsed.Select(report => ToEntity(report, link)).ToList();
        var unidentified = reports.Count(report => report == null);
        if (unidentified > 0)
            _logger.LogWarning(
                "N-CEN data set {Year} Q{Quarter}: skipped {Count} reports without a usable CIK",
                link.Year,
                link.Quarter,
                unidentified
            );
        reports.RemoveAll(report => report == null);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );

            // SEC has never listed an accession in two quarters; if it does, refuse the quarter
            // rather than guess which copy wins, since a later re-post could silently drop it.
            var accessions = reports.Select(report => report.AccessionNumber).ToList();
            var elsewhere = await dbContext
                .Set<NCenRegistrantReport>()
                .Where(report =>
                    accessions.Contains(report.AccessionNumber)
                    && (report.DataSetYear != link.Year || report.DataSetQuarter != link.Quarter)
                )
                .Select(report => new
                {
                    report.AccessionNumber,
                    report.DataSetYear,
                    report.DataSetQuarter,
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (elsewhere != null)
                throw new InvalidDataException(
                    $"Accession {elsewhere.AccessionNumber} was already imported from {elsewhere.DataSetYear} Q{elsewhere.DataSetQuarter}."
                );

            var replaced = dbContext
                .Set<NCenRegistrantReport>()
                .Where(report =>
                    report.DataSetYear == link.Year && report.DataSetQuarter == link.Quarter
                );
            var ciks = (
                await replaced
                    .Select(report => report.RegistrantCik)
                    .Distinct()
                    .ToListAsync(cancellationToken)
            ).ToHashSet(StringComparer.Ordinal);
            await replaced.ExecuteDeleteAsync(cancellationToken);

            foreach (var batch in reports.Chunk(InsertBatchSize))
            {
                dbContext.AddRange(batch);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                ciks.UnionWith(batch.Select(report => report.RegistrantCik));
            }

            await MarkSuperseded(dbContext, ciks.ToList(), cancellationToken);

            var ledger = await dbContext
                .Set<NCenDataSetImport>()
                .SingleOrDefaultAsync(
                    row => row.Year == link.Year && row.Quarter == link.Quarter,
                    cancellationToken
                );
            if (ledger == null)
            {
                ledger = new NCenDataSetImport { Year = link.Year, Quarter = link.Quarter };
                dbContext.Add(ledger);
            }
            ledger.FileName = link.FileName;
            ledger.ParserVersion = ParserVersion;
            ledger.ReportCount = reports.Count;
            ledger.ImportedAt = DateTime.UtcNow;
            ledger.LastFailureAt = null;
            ledger.LastFailure = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return reports.Count;
        });
    }

    // A fund is current for its registrant and period until a later filing (by filing date, then
    // accession) reports the same series; series an amendment omits stay current.
    private static Task MarkSuperseded(
        EquiblesFinancialDbContext dbContext,
        List<string> ciks,
        CancellationToken cancellationToken
    )
    {
        var funds = dbContext.Set<NCenFundReport>();
        // Only rows whose flag changes are written, so re-marking a registrant leaves the rest alone.
        return funds
            .Where(fund => ciks.Contains(fund.RegistrantCik))
            .Where(fund =>
                fund.IsSuperseded
                != funds.Any(other =>
                    other.RegistrantCik == fund.RegistrantCik
                    && other.ReportEndingPeriod == fund.ReportEndingPeriod
                    && other.SeriesId == fund.SeriesId
                    && (
                        other.FilingDate > fund.FilingDate
                        || other.FilingDate == fund.FilingDate
                            && string.Compare(other.AccessionNumber, fund.AccessionNumber) > 0
                    )
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(fund => fund.IsSuperseded, fund => !fund.IsSuperseded),
                cancellationToken
            );
    }

    private static NCenRegistrantReport ToEntity(
        NCenRegistrantReportData data,
        NCenDataSetLink link
    )
    {
        var cik = CikNormalizer.Canonicalize(data.Cik);
        if (cik == null)
            return null;
        var report = new NCenRegistrantReport
        {
            AccessionNumber = data.AccessionNumber,
            RegistrantCik = cik,
            RegistrantName = data.RegistrantName.TruncateToFit(512),
            InvestmentCompanyFileNumber = data.InvestmentCompanyFileNumber.TruncateToFit(32),
            RegistrantLei = ValidLei(data.Lei),
            InvestmentCompanyType = data.InvestmentCompanyType.TruncateToFit(16),
            FilingDate = data.FilingDate,
            ReportEndingPeriod = data.ReportEndingPeriod,
            IsAmendment = data.IsAmendment,
            IsFamilyInvestmentCompany = data.IsFamilyInvestmentCompany,
            FamilyInvestmentCompanyName = data.FamilyInvestmentCompanyName.TruncateToFit(256),
            TotalSeries = data.TotalSeries,
            DataSetYear = link.Year,
            DataSetQuarter = link.Quarter,
        };
        foreach (var fund in data.Funds)
        {
            var entity = new NCenFundReport
            {
                RegistrantReportId = report.Id,
                FundId = fund.FundId.TruncateToFit(64),
                SeriesId = fund.SeriesId.TruncateToFit(16),
                FundName = fund.FundName.TruncateToFit(512),
                FundLei = ValidLei(fund.Lei),
                RegistrantCik = cik,
                AccessionNumber = data.AccessionNumber,
                FilingDate = data.FilingDate,
                ReportEndingPeriod = data.ReportEndingPeriod,
                FundTypes = fund.FundTypes,
                IsIndexAffiliated = fund.IsIndexAffiliated,
                IsIndexExclusive = fund.IsIndexExclusive,
                MonthlyAverageNetAssets = fund.MonthlyAverageNetAssets,
                DailyAverageNetAssets = fund.DailyAverageNetAssets,
            };
            entity.Advisers.AddRange(
                fund.Advisers.Select(adviser => new NCenFundAdviser
                {
                    FundReportId = entity.Id,
                    Role = adviser.Role,
                    Name = adviser.Name.TruncateToFit(512),
                    FileNumber = adviser.FileNumber.TruncateToFit(32),
                    Crd = adviser.Crd,
                    Lei = ValidLei(adviser.Lei),
                    State = adviser.State.TruncateToFit(16),
                    Country = adviser.Country.TruncateToFit(16),
                    IsAffiliated = adviser.IsAffiliated,
                    IsHiredDuringPeriod = adviser.IsHiredDuringPeriod,
                    StartDate = adviser.StartDate,
                    TerminationDate = adviser.TerminationDate,
                })
            );
            entity.ShareClasses.AddRange(
                fund.ShareClasses.Select(shareClass => new NCenFundShareClass
                {
                    FundReportId = entity.Id,
                    ClassId = shareClass.ClassId.TruncateToFit(16),
                    ClassName = shareClass.ClassName.TruncateToFit(256),
                    Ticker = shareClass.Ticker.TruncateToFit(16),
                })
            );
            entity.ExchangeListings.AddRange(
                fund.ExchangeListings.Select(listing => new NCenFundExchangeListing
                {
                    FundReportId = entity.Id,
                    Exchange = listing.Exchange.TruncateToFit(16),
                    Ticker = listing.Ticker.TruncateToFit(16),
                })
            );
            report.Funds.Add(entity);
        }
        return report;
    }

    private static string ValidLei(string value) =>
        InternationalSecurityIdentifiers.IsValidLei(value) ? value : null;
}

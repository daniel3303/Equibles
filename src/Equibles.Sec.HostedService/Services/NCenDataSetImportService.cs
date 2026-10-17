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

        var due = catalog
            .Where(link =>
                !ledger.TryGetValue((link.Year, link.Quarter), out var imported)
                || imported.FileName != link.FileName
                || imported.ParserVersion < ParserVersion
            )
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
            catch (InvalidDataException exception)
            {
                // A malformed archive stays unrecorded and is retried; later quarters still import.
                _logger.LogError(
                    exception,
                    "N-CEN data set {Year} Q{Quarter} is unreadable",
                    link.Year,
                    link.Quarter
                );
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

    private async Task<int> ImportDataSet(NCenDataSetLink link, CancellationToken cancellationToken)
    {
        List<NCenRegistrantReportData> reports;
        await using (var zipStream = await _secEdgarClient.DownloadStream(link.Url))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            reports = NCenDataSetParser.Parse(archive);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );

            // Registrants of a replaced copy are re-marked too, in case the re-post dropped them.
            var quarterReports = dbContext
                .Set<NCenRegistrantReport>()
                .Where(report =>
                    report.DataSetYear == link.Year && report.DataSetQuarter == link.Quarter
                );
            var ciks = (
                await quarterReports
                    .Select(report => report.RegistrantCik)
                    .Distinct()
                    .ToListAsync(cancellationToken)
            ).ToHashSet(StringComparer.Ordinal);
            await quarterReports.ExecuteDeleteAsync(cancellationToken);

            // An accession SEC already published in another quarter keeps that copy.
            var accessions = reports.Select(report => report.AccessionNumber).ToList();
            var storedElsewhere = (
                await dbContext
                    .Set<NCenRegistrantReport>()
                    .Where(report => accessions.Contains(report.AccessionNumber))
                    .Select(report => report.AccessionNumber)
                    .ToListAsync(cancellationToken)
            ).ToHashSet(StringComparer.Ordinal);

            var stored = 0;
            foreach (
                var batch in reports
                    .Where(report => !storedElsewhere.Contains(report.AccessionNumber))
                    .Select(report => ToEntity(report, link))
                    .Where(report => report != null)
                    .Chunk(InsertBatchSize)
            )
            {
                dbContext.AddRange(batch);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                stored += batch.Length;
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
            ledger.ReportCount = stored;
            ledger.ImportedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return stored;
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
        return funds
            .Where(fund => ciks.Contains(fund.RegistrantCik))
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        fund => fund.IsSuperseded,
                        fund =>
                            funds.Any(other =>
                                other.RegistrantCik == fund.RegistrantCik
                                && other.ReportEndingPeriod == fund.ReportEndingPeriod
                                && other.SeriesId == fund.SeriesId
                                && (
                                    other.FilingDate > fund.FilingDate
                                    || other.FilingDate == fund.FilingDate
                                        && string.Compare(
                                            other.AccessionNumber,
                                            fund.AccessionNumber
                                        ) > 0
                                )
                            )
                    ),
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

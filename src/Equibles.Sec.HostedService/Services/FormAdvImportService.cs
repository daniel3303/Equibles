using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.FormAdv;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Imports the SEC's bulk Form ADV adviser download. The SEC publishes one snapshot per month
/// as <c>ia&lt;MMDDYY&gt;.zip</c> (always the first of the month, a month or two in arrears), so
/// the importer probes recent months newest-first, downloads the most recent one available, and
/// upserts every adviser keyed by Organization CRD number. Re-running once a snapshot is already
/// stored is a no-op.
/// </summary>
[Service]
public class FormAdvImportService : IImporter
{
    private const string BaseUrl =
        "https://www.sec.gov/files/investment/data/other/information-about-registered-investment-advisers-exempt-reporting-advisers";
    private const int MonthsToProbe = 4;
    private const int UpsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FormAdvImportService> _logger;
    private readonly ErrorReporter _errorReporter;

    public FormAdvImportService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<FormAdvImportService> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var storedLatest = await GetStoredLatestReportDate(cancellationToken);

        foreach (var fileDate in GetCandidateFileDates())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = $"ia{fileDate.ToString("MMddyy", CultureInfo.InvariantCulture)}.zip";
            Stream zipStream;
            try
            {
                zipStream = await _secEdgarClient.DownloadStream($"{BaseUrl}/{fileName}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // This month's snapshot is not published yet — try the previous one.
                continue;
            }

            await using (zipStream)
            {
                // The newest snapshot that exists. If it is already stored, nothing to do.
                if (storedLatest.HasValue && storedLatest.Value >= fileDate)
                {
                    _logger.LogInformation(
                        "Form ADV data is up to date (latest snapshot {Date})",
                        fileDate
                    );
                    return;
                }

                _logger.LogInformation("Importing Form ADV snapshot {Date}", fileDate);
                var imported = await ImportSnapshot(zipStream, fileDate, cancellationToken);
                _logger.LogInformation(
                    "Form ADV {Date}: upserted {Count} advisers",
                    fileDate,
                    imported
                );
            }

            return;
        }

        _logger.LogWarning(
            "No Form ADV snapshot found in the last {Months} months — SEC URL or schedule may have changed",
            MonthsToProbe
        );
        await _errorReporter.Report(
            ErrorSource.FormAdvScraper,
            "FormAdvImport.NoSnapshot",
            $"No Form ADV snapshot found in the last {MonthsToProbe} months — SEC URL or schedule may have changed",
            null
        );
    }

    private async Task<DateOnly?> GetStoredLatestReportDate(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<FormAdvAdviserRepository>();
        var dates = await repo.GetAll()
            .OrderByDescending(a => a.ReportDate)
            .Select(a => (DateOnly?)a.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        return dates;
    }

    private async Task<int> ImportSnapshot(
        Stream zipStream,
        DateOnly fileDate,
        CancellationToken cancellationToken
    )
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        );

        if (entry == null)
        {
            _logger.LogError(
                "Form ADV snapshot {Date} contained no CSV entry — SEC format may have changed",
                fileDate
            );
            await _errorReporter.Report(
                ErrorSource.FormAdvScraper,
                "FormAdvImport.NoCsvEntry",
                $"Form ADV snapshot {fileDate} contained no CSV entry — SEC format may have changed",
                null
            );
            return 0;
        }

        await using var entryStream = entry.Open();
        // The SEC publishes the CSV in Latin-1; reading it as UTF-8 would corrupt accented names.
        using var reader = new StreamReader(entryStream, Encoding.Latin1);

        var now = DateTime.UtcNow;
        var advisers = FormAdvCsvParser.Parse(reader).Select(d => ToEntity(d, fileDate, now));

        return await BatchPersister.Persist(advisers, UpsertBatchSize, FlushBatch);
    }

    private async Task FlushBatch(List<FormAdvAdviser> batch)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        await dbContext
            .Set<FormAdvAdviser>()
            .UpsertRange(batch)
            .On(a => a.Crd)
            .WhenMatched(
                (existing, incoming) =>
                    new FormAdvAdviser
                    {
                        SecNumber = incoming.SecNumber,
                        LegalName = incoming.LegalName,
                        PrimaryBusinessName = incoming.PrimaryBusinessName,
                        MainOfficeCity = incoming.MainOfficeCity,
                        MainOfficeState = incoming.MainOfficeState,
                        MainOfficeCountry = incoming.MainOfficeCountry,
                        WebsiteAddress = incoming.WebsiteAddress,
                        SecStatus = incoming.SecStatus,
                        NumberOfEmployees = incoming.NumberOfEmployees,
                        TotalRegulatoryAum = incoming.TotalRegulatoryAum,
                        DiscretionaryAum = incoming.DiscretionaryAum,
                        NonDiscretionaryAum = incoming.NonDiscretionaryAum,
                        ChargesPercentageOfAum = incoming.ChargesPercentageOfAum,
                        ChargesHourly = incoming.ChargesHourly,
                        ChargesSubscription = incoming.ChargesSubscription,
                        ChargesFixed = incoming.ChargesFixed,
                        ChargesCommissions = incoming.ChargesCommissions,
                        ChargesPerformanceBased = incoming.ChargesPerformanceBased,
                        ChargesOther = incoming.ChargesOther,
                        ReportDate = incoming.ReportDate,
                        UpdateTime = incoming.UpdateTime,
                    }
            )
            .RunAsync();
    }

    private static FormAdvAdviser ToEntity(
        FormAdvAdviserData data,
        DateOnly fileDate,
        DateTime now
    ) =>
        new()
        {
            Crd = data.Crd,
            SecNumber = data.SecNumber,
            LegalName = data.LegalName,
            PrimaryBusinessName = data.PrimaryBusinessName,
            MainOfficeCity = data.MainOfficeCity,
            MainOfficeState = data.MainOfficeState,
            MainOfficeCountry = data.MainOfficeCountry,
            WebsiteAddress = data.WebsiteAddress,
            SecStatus = data.SecStatus,
            NumberOfEmployees = data.NumberOfEmployees,
            TotalRegulatoryAum = data.TotalRegulatoryAum,
            DiscretionaryAum = data.DiscretionaryAum,
            NonDiscretionaryAum = data.NonDiscretionaryAum,
            ChargesPercentageOfAum = data.ChargesPercentageOfAum,
            ChargesHourly = data.ChargesHourly,
            ChargesSubscription = data.ChargesSubscription,
            ChargesFixed = data.ChargesFixed,
            ChargesCommissions = data.ChargesCommissions,
            ChargesPerformanceBased = data.ChargesPerformanceBased,
            ChargesOther = data.ChargesOther,
            ReportDate = fileDate,
            CreationTime = now,
            UpdateTime = now,
        };

    /// <summary>The first of the month for the current month and the prior months, newest first.</summary>
    internal static IEnumerable<DateOnly> GetCandidateFileDates()
    {
        var firstOfThisMonth = new DateOnly(
            DateOnly.FromDateTime(DateTime.UtcNow).Year,
            DateOnly.FromDateTime(DateTime.UtcNow).Month,
            1
        );
        for (var i = 0; i < MonthsToProbe; i++)
        {
            yield return firstOfThisMonth.AddMonths(-i);
        }
    }
}

using System.IO.Compression;
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

[Service]
public class FormAdvImportService : IImporter
{
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

        await using var catalogStream = await _secEdgarClient.DownloadStream(
            FormAdvSnapshotCatalog.PageUrl
        );
        using var reader = new StreamReader(catalogStream, Encoding.UTF8);
        var catalog = await reader.ReadToEndAsync(cancellationToken);
        var snapshot = FormAdvSnapshotCatalog.Latest(catalog);
        if (storedLatest.HasValue && storedLatest.Value >= snapshot.ReportDate)
        {
            _logger.LogInformation(
                "Form ADV data is up to date (latest snapshot {Date})",
                snapshot.ReportDate
            );
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var zipStream = await _secEdgarClient.DownloadStream(snapshot.Url);
        var imported = await ImportSnapshot(zipStream, snapshot.ReportDate, cancellationToken);
        _logger.LogInformation(
            "Form ADV {Date}: upserted {Count} advisers",
            snapshot.ReportDate,
            imported
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
}

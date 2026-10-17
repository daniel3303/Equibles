using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.Core.AutoWiring;
using Equibles.Core.Identity;
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

    /// <summary>
    /// Bump when the parser reads more of the roster: the newest snapshot of each roster is
    /// re-imported once. 1: client types, private funds, CIK, LEI, Items 2/3/6/7 and the
    /// exempt reporting adviser roster.
    /// </summary>
    internal const int ParserVersion = 1;

    private static readonly FormAdvRegistration[] Rosters =
    [
        FormAdvRegistration.Registered,
        FormAdvRegistration.ExemptReporting,
    ];

    private static readonly Dictionary<string, FormAdvOrganizationForm> OrganizationForms = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Corporation"] = FormAdvOrganizationForm.Corporation,
        ["Sole Proprietorship"] = FormAdvOrganizationForm.SoleProprietorship,
        ["Limited Liability Partnership"] = FormAdvOrganizationForm.LimitedLiabilityPartnership,
        ["Partnership"] = FormAdvOrganizationForm.Partnership,
        ["Limited Liability Company"] = FormAdvOrganizationForm.LimitedLiabilityCompany,
        ["Limited Partnership"] = FormAdvOrganizationForm.LimitedPartnership,
        ["Other (specify)"] = FormAdvOrganizationForm.Other,
        ["Other"] = FormAdvOrganizationForm.Other,
    };

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
        await using var catalogStream = await _secEdgarClient.DownloadStream(
            FormAdvSnapshotCatalog.PageUrl
        );
        using var reader = new StreamReader(catalogStream, Encoding.UTF8);
        var catalog = await reader.ReadToEndAsync(cancellationToken);

        foreach (var registration in Rosters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = FormAdvSnapshotCatalog.Latest(catalog, registration);
            if (await IsImported(registration, snapshot.ReportDate, cancellationToken))
            {
                _logger.LogInformation(
                    "Form ADV {Roster} roster is up to date (latest snapshot {Date})",
                    registration,
                    snapshot.ReportDate
                );
                continue;
            }

            await using var zipStream = await _secEdgarClient.DownloadStream(snapshot.Url);
            var imported = await ImportSnapshot(
                zipStream,
                registration,
                snapshot.ReportDate,
                cancellationToken
            );
            if (imported > 0)
                await RecordImport(registration, snapshot.ReportDate, imported);
            _logger.LogInformation(
                "Form ADV {Roster} {Date}: upserted {Count} advisers",
                registration,
                snapshot.ReportDate,
                imported
            );
        }
    }

    private async Task<bool> IsImported(
        FormAdvRegistration registration,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<FormAdvRosterImportRepository>();
        return await repo.GetByRoster(registration, reportDate)
            .AnyAsync(import => import.ParserVersion >= ParserVersion, cancellationToken);
    }

    private async Task RecordImport(
        FormAdvRegistration registration,
        DateOnly reportDate,
        int adviserCount
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<FormAdvRosterImportRepository>();
        var import = await repo.GetByRoster(registration, reportDate).SingleOrDefaultAsync();
        if (import == null)
        {
            import = new FormAdvRosterImport
            {
                Registration = registration,
                ReportDate = reportDate,
            };
            repo.Add(import);
        }
        import.ParserVersion = ParserVersion;
        import.AdviserCount = adviserCount;
        import.ImportedAt = DateTime.UtcNow;
        await repo.SaveChanges();
    }

    private async Task<int> ImportSnapshot(
        Stream zipStream,
        FormAdvRegistration registration,
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
                "Form ADV {Roster} snapshot {Date} contained no CSV entry — SEC format may have changed",
                registration,
                fileDate
            );
            await _errorReporter.Report(
                ErrorSource.FormAdvScraper,
                "FormAdvImport.NoCsvEntry",
                $"Form ADV {registration} snapshot {fileDate} contained no CSV entry — SEC format may have changed",
                null
            );
            return 0;
        }

        await using var entryStream = entry.Open();
        // The SEC publishes the CSV in Latin-1; reading it as UTF-8 would corrupt accented names.
        using var reader = new StreamReader(entryStream, Encoding.Latin1);

        var mismatched = 0;
        var advisers = FormAdvCsvParser
            .Parse(reader)
            .Where(data =>
            {
                if (IsListedIn(data, registration))
                    return true;
                mismatched++;
                return false;
            });
        var imported = await BatchPersister.Persist(
            advisers,
            UpsertBatchSize,
            batch => FlushBatch(batch, registration, fileDate)
        );
        if (mismatched > 0)
            _logger.LogWarning(
                "Form ADV {Roster} {Date}: skipped {Count} rows whose firm type belongs to the other roster",
                registration,
                fileDate,
                mismatched
            );
        return imported;
    }

    // Both rosters carry a Firm Type column; a row naming the other roster is never filed under this one.
    private static bool IsListedIn(FormAdvAdviserData data, FormAdvRegistration registration) =>
        data.FirmType == null
        || string.Equals(
            data.FirmType,
            registration == FormAdvRegistration.Registered ? "Registered" : "ERA",
            StringComparison.OrdinalIgnoreCase
        );

    private async Task FlushBatch(
        List<FormAdvAdviserData> batch,
        FormAdvRegistration registration,
        DateOnly fileDate
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var now = DateTime.UtcNow;
        var advisers = batch.Select(data => ToEntity(data, registration, fileDate, now)).ToList();
        var clientTypes = batch
            .SelectMany(data =>
                data.ClientTypes.Select(clientType => ToEntity(data.Crd, clientType, fileDate))
            )
            .ToList();
        var crds = advisers.Select(adviser => adviser.Crd).ToList();

        // An adviser's client types always come from its latest roster, so a type it stopped
        // reporting (or a move to the exempt roster, which has no Item 5) leaves no stale row.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext
            .Set<FormAdvAdviser>()
            .UpsertRange(advisers)
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
                        Registration = incoming.Registration,
                        Cik = incoming.Cik,
                        CikCount = incoming.CikCount,
                        Lei = incoming.Lei,
                        LatestFilingDate = incoming.LatestFilingDate,
                        OrganizationForm = incoming.OrganizationForm,
                        RegistrationBasis = incoming.RegistrationBasis,
                        ExemptionBasis = incoming.ExemptionBasis,
                        OtherBusinesses = incoming.OtherBusinesses,
                        RelatedPersons = incoming.RelatedPersons,
                        AdvisesPrivateFunds = incoming.AdvisesPrivateFunds,
                        PrivateFundCount = incoming.PrivateFundCount,
                        HedgeFundCount = incoming.HedgeFundCount,
                        PrivateEquityFundCount = incoming.PrivateEquityFundCount,
                        VentureCapitalFundCount = incoming.VentureCapitalFundCount,
                        RealEstateFundCount = incoming.RealEstateFundCount,
                        LiquidityFundCount = incoming.LiquidityFundCount,
                        SecuritizedAssetFundCount = incoming.SecuritizedAssetFundCount,
                        OtherPrivateFundCount = incoming.OtherPrivateFundCount,
                        PrivateFundGrossAssets = incoming.PrivateFundGrossAssets,
                        ReportDate = incoming.ReportDate,
                        UpdateTime = incoming.UpdateTime,
                    }
            )
            .RunAsync();
        await dbContext
            .Set<FormAdvAdviserClientType>()
            .Where(clientType => crds.Contains(clientType.Crd))
            .ExecuteDeleteAsync();
        dbContext.Set<FormAdvAdviserClientType>().AddRange(clientTypes);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static FormAdvAdviser ToEntity(
        FormAdvAdviserData data,
        FormAdvRegistration registration,
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
            Registration = registration,
            Cik = CikNormalizer.Canonicalize(data.Cik),
            CikCount = data.CikCount,
            Lei = ValidLei(data.Lei),
            LatestFilingDate = data.LatestFilingDate,
            OrganizationForm =
                data.OrganizationForm != null
                && OrganizationForms.TryGetValue(data.OrganizationForm, out var form)
                    ? form
                    : null,
            RegistrationBasis = (FormAdvRegistrationBasis)data.RegistrationBasisItems,
            ExemptionBasis = (FormAdvExemptionBasis)data.ExemptionBasisItems,
            OtherBusinesses = (FormAdvOtherBusiness)data.OtherBusinessItems,
            RelatedPersons = (FormAdvRelatedPerson)data.RelatedPersonItems,
            AdvisesPrivateFunds = data.AdvisesPrivateFunds,
            PrivateFundCount = data.PrivateFundCount,
            HedgeFundCount = data.HedgeFundCount,
            PrivateEquityFundCount = data.PrivateEquityFundCount,
            VentureCapitalFundCount = data.VentureCapitalFundCount,
            RealEstateFundCount = data.RealEstateFundCount,
            LiquidityFundCount = data.LiquidityFundCount,
            SecuritizedAssetFundCount = data.SecuritizedAssetFundCount,
            OtherPrivateFundCount = data.OtherPrivateFundCount,
            PrivateFundGrossAssets = data.PrivateFundGrossAssets,
            ReportDate = fileDate,
            CreationTime = now,
            UpdateTime = now,
        };

    private static FormAdvAdviserClientType ToEntity(
        int crd,
        FormAdvClientTypeData clientType,
        DateOnly fileDate
    ) =>
        new()
        {
            Crd = crd,
            ClientType = (FormAdvClientType)clientType.Index,
            ClientCount = clientType.ClientCount,
            FewerThanFiveClients = clientType.FewerThanFiveClients,
            RegulatoryAum = clientType.RegulatoryAum,
            ReportDate = fileDate,
        };

    // Item 1.P is free text: placeholders ("N/A", "NONE") and mistyped codes fail the ISO 17442 check.
    private static string ValidLei(string value)
    {
        var lei = value?.Trim().ToUpperInvariant();
        return InternationalSecurityIdentifiers.IsValidLei(lei) ? lei : null;
    }
}

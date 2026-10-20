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

    // What one roster pass writes: the current adviser view, the version history, or both.
    [Flags]
    private enum RosterWrite
    {
        None = 0,
        CurrentView = 1,
        History = 2,
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FormAdvImportService> _logger;
    private readonly ErrorReporter _errorReporter;
    private readonly FormAdvPrivateFundArchiveImporter _archiveImporter;

    public FormAdvImportService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<FormAdvImportService> logger,
        ErrorReporter errorReporter,
        FormAdvPrivateFundArchiveImporter archiveImporter
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _errorReporter = errorReporter;
        _archiveImporter = archiveImporter;
    }

    /// <summary>
    /// The first roster the version history reads. SEC publishes exempt reporting adviser rosters
    /// only from February 2024, so that history starts there.
    /// </summary>
    internal static readonly DateOnly HistoryStart = new(2019, 12, 1);

    public async Task Import(CancellationToken cancellationToken) =>
        await ImportCycle(cancellationToken);

    /// <summary>
    /// Brings the current adviser view up to the newest rosters, then takes one backfill step: the
    /// oldest roster not yet in the version history, else one part of the Schedule D archive.
    /// Returns true while backfill work remains.
    /// </summary>
    public async Task<bool> ImportCycle(CancellationToken cancellationToken)
    {
        await using var catalogStream = await _secEdgarClient.DownloadStream(
            FormAdvSnapshotCatalog.PageUrl
        );
        using var reader = new StreamReader(catalogStream, Encoding.UTF8);
        var catalog = await reader.ReadToEndAsync(cancellationToken);

        var published = Rosters.ToDictionary(
            registration => registration,
            registration => FormAdvSnapshotCatalog.All(catalog, registration, HistoryStart)
        );
        await RecordPublished(published, cancellationToken);

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

            // The history takes this roster in the same pass only when every earlier one is in.
            var history = await NextHistoryRoster(registration, published, cancellationToken);
            var versioned = history?.ReportDate == snapshot.ReportDate;
            var previous = versioned
                ? await PreviousVersionedRoster(registration, snapshot.ReportDate)
                : null;
            await using var download = await _secEdgarClient.DownloadStream(snapshot.Url);
            var imported = await ImportSnapshot(
                download,
                snapshot,
                registration,
                RosterWrite.CurrentView | (versioned ? RosterWrite.History : RosterWrite.None),
                previous,
                cancellationToken
            );
            if (imported > 0)
                await RecordImport(registration, snapshot.ReportDate, imported.Value, versioned);
            _logger.LogInformation(
                "Form ADV {Roster} {Date}: upserted {Count} advisers{History}",
                registration,
                snapshot.ReportDate,
                imported,
                versioned ? " and folded them into the version history" : ""
            );
        }

        if (await VersionNextRoster(published, cancellationToken))
            return true;
        return await _archiveImporter.ImportNext(cancellationToken);
    }

    // Every roster SEC lists from the history start gets a ledger row, so a point-in-time read
    // knows which roster was current at a date even before the walk reaches it.
    private async Task RecordPublished(
        Dictionary<FormAdvRegistration, List<FormAdvSnapshot>> published,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var known = await dbContext
            .Set<FormAdvRosterImport>()
            .Select(import => new { import.Registration, import.ReportDate })
            .ToListAsync(cancellationToken);
        var knownKeys = known.Select(row => (row.Registration, row.ReportDate)).ToHashSet();
        var added = published
            .SelectMany(pair => pair.Value.Select(snapshot => (pair.Key, snapshot.ReportDate)))
            .Where(key => !knownKeys.Contains(key))
            .Select(key => new FormAdvRosterImport
            {
                Registration = key.Key,
                ReportDate = key.ReportDate,
            })
            .ToList();
        if (added.Count == 0)
            return;
        dbContext.Set<FormAdvRosterImport>().AddRange(added);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // The oldest roster of a kind after the newest one in the history, if SEC lists one.
    private async Task<FormAdvSnapshot> NextHistoryRoster(
        FormAdvRegistration registration,
        Dictionary<FormAdvRegistration, List<FormAdvSnapshot>> published,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var frontier = await dbContext
            .Set<FormAdvRosterImport>()
            .Where(import => import.Registration == registration && import.VersionedAt != null)
            .MaxAsync(import => (DateOnly?)import.ReportDate, cancellationToken);
        return published[registration]
            .FirstOrDefault(snapshot => frontier == null || snapshot.ReportDate > frontier);
    }

    // The newest roster of a kind before this one that added advisers to the history.
    private async Task<DateOnly?> PreviousVersionedRoster(
        FormAdvRegistration registration,
        DateOnly reportDate
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        return await dbContext
            .Set<FormAdvRosterImport>()
            .Where(import =>
                import.Registration == registration
                && import.ReportDate < reportDate
                && import.VersionedAt != null
                && import.AdviserCount > 0
            )
            .MaxAsync(import => (DateOnly?)import.ReportDate);
    }

    // One backfill roster per cycle, the oldest of either kind; true when one was read.
    private async Task<bool> VersionNextRoster(
        Dictionary<FormAdvRegistration, List<FormAdvSnapshot>> published,
        CancellationToken cancellationToken
    )
    {
        var pending = new List<(FormAdvRegistration Registration, FormAdvSnapshot Snapshot)>();
        foreach (var registration in Rosters)
        {
            if (await NextHistoryRoster(registration, published, cancellationToken) is { } snapshot)
                pending.Add((registration, snapshot));
        }
        if (pending.Count == 0)
            return false;

        var (next, roster) = pending.MinBy(item => item.Snapshot.ReportDate);
        var previous = await PreviousVersionedRoster(next, roster.ReportDate);
        await using var download = await _secEdgarClient.DownloadStream(roster.Url);
        var count = await ImportSnapshot(
            download,
            roster,
            next,
            RosterWrite.History,
            previous,
            cancellationToken
        );
        // An archive without a roster entry is passed over like a month without data; one whose
        // entry yields no adviser means the format changed, so the walk stops until it is read.
        if (count == 0)
            throw new InvalidDataException(
                $"The Form ADV {next} roster of {roster.ReportDate} has no adviser this parser can read."
            );
        await RecordVersioned(next, roster.ReportDate, count ?? 0);
        _logger.LogInformation(
            "Form ADV {Roster} {Date}: folded {Count} advisers into the version history",
            next,
            roster.ReportDate,
            count
        );
        return true;
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
        int adviserCount,
        bool versioned
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
        if (versioned)
            import.VersionedAt = DateTime.UtcNow;
        await repo.SaveChanges();
    }

    // A roster with no readable entry is marked read with no advisers, so the walk moves past it
    // and point-in-time reads treat its month like one SEC published no data for.
    private async Task RecordVersioned(
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
        // A roster the current view already imported keeps its own count.
        if (import.ParserVersion == 0)
            import.AdviserCount = adviserCount;
        import.VersionedAt = DateTime.UtcNow;
        await repo.SaveChanges();
    }

    // The advisers read, or null when the download holds no roster entry.
    private async Task<int?> ImportSnapshot(
        Stream download,
        FormAdvSnapshot snapshot,
        FormAdvRegistration registration,
        RosterWrite write,
        DateOnly? previousVersioned,
        CancellationToken cancellationToken
    )
    {
        var records = FormAdvRosterFile.Open(download, snapshot.Url);
        if (records == null)
        {
            _logger.LogError(
                "Form ADV {Roster} snapshot {Date} contained no CSV or workbook entry — SEC format may have changed",
                registration,
                snapshot.ReportDate
            );
            await _errorReporter.Report(
                ErrorSource.FormAdvScraper,
                "FormAdvImport.NoCsvEntry",
                $"Form ADV {registration} snapshot {snapshot.ReportDate} contained no CSV or workbook entry — SEC format may have changed",
                null
            );
            return null;
        }

        var mismatched = 0;
        var advisers = FormAdvCsvParser
            .Parse(records)
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
            batch =>
                FlushBatch(
                    batch,
                    registration,
                    snapshot.ReportDate,
                    write,
                    previousVersioned,
                    cancellationToken
                )
        );
        if (mismatched > 0)
            _logger.LogWarning(
                "Form ADV {Roster} {Date}: skipped {Count} rows whose firm type belongs to the other roster",
                registration,
                snapshot.ReportDate,
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
        DateOnly fileDate,
        RosterWrite write,
        DateOnly? previousVersioned,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        var clientTypesByCrd = clientTypes.ToLookup(clientType => clientType.Crd);
        var versions = write.HasFlag(RosterWrite.History)
            ? advisers
                .Select(adviser =>
                    FormAdvVersionContent.From(
                        adviser,
                        clientTypesByCrd[adviser.Crd],
                        registration,
                        fileDate
                    )
                )
                .ToList()
            : [];
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            if (write.HasFlag(RosterWrite.CurrentView))
                await UpsertCurrentView(dbContext, advisers, clientTypes, crds, fileDate);
            if (write.HasFlag(RosterWrite.History))
                await FormAdvVersionWriter.Write(
                    dbContext,
                    versions,
                    registration,
                    fileDate,
                    previousVersioned
                );
            await transaction.CommitAsync();
        });
    }

    private static async Task UpsertCurrentView(
        EquiblesFinancialDbContext dbContext,
        List<FormAdvAdviser> advisers,
        List<FormAdvAdviserClientType> clientTypes,
        List<int> crds,
        DateOnly fileDate
    )
    {
        // A roster never overwrites an adviser a newer roster already listed (the exempt
        // roster can lag the registered one), and client types follow the row that won, so
        // a type the adviser stopped reporting leaves no stale row.
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
                        LastSeenReportDate = incoming.LastSeenReportDate,
                        UpdateTime = incoming.UpdateTime,
                    }
            )
            .UpdateIf((existing, incoming) => existing.ReportDate <= incoming.ReportDate)
            .RunAsync();
        var current = await dbContext
            .Set<FormAdvAdviser>()
            .Where(adviser => crds.Contains(adviser.Crd) && adviser.ReportDate == fileDate)
            .Select(adviser => adviser.Crd)
            .ToListAsync();
        await dbContext
            .Set<FormAdvAdviserClientType>()
            .Where(clientType => current.Contains(clientType.Crd))
            .ExecuteDeleteAsync();
        var currentCrds = current.ToHashSet();
        dbContext
            .Set<FormAdvAdviserClientType>()
            .AddRange(clientTypes.Where(clientType => currentCrds.Contains(clientType.Crd)));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
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
            LastSeenReportDate = fileDate,
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

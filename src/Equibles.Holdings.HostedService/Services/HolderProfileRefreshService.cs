using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Recomputes every holder's classification evidence and profile from the regulatory records on
/// file (Form ADV rosters, SEC filer profiles, 13D/13G cover pages, N-CEN reports) and replaces
/// the stored set in one transaction, so readers always see one consistent generation.
/// </summary>
[Service]
public class HolderProfileRefreshService
{
    // Each save is bounded so the change tracker never holds the whole generation.
    private const int WriteBatchSize = 5_000;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<HolderProfileRefreshService> _logger;

    public HolderProfileRefreshService(
        EquiblesFinancialDbContext dbContext,
        ILogger<HolderProfileRefreshService> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>Returns the number of profiles written.</summary>
    public async Task<int> Run(DateOnly today, CancellationToken cancellationToken)
    {
        var holders = await _dbContext
            .Set<InstitutionalHolder>()
            .AsNoTracking()
            .Select(holder => new HolderIdentityRow(
                holder.Id,
                holder.Cik,
                holder.CrdNumber,
                holder.SecFileNumber
            ))
            .ToListAsync(cancellationToken);

        var advisers = await _dbContext
            .Set<FormAdvAdviser>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var sources = HolderProfileSources.Build(
            advisers,
            await _dbContext
                .Set<FormAdvAdviserClientType>()
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            await _dbContext
                .Set<SecFilerProfile>()
                .AsNoTracking()
                .Where(profile => profile.Found)
                .ToListAsync(cancellationToken),
            await LoadBeneficialOwnership(today, cancellationToken),
            await LoadRegistrants(today, cancellationToken),
            await AdvisedSeriesLoader.Load(_dbContext, today, cancellationToken)
        );
        var context = new HolderEvidenceContext(
            today,
            await LatestRoster(FormAdvRegistration.Registered, advisers, cancellationToken),
            await LatestRoster(FormAdvRegistration.ExemptReporting, advisers, cancellationToken)
        );

        var profiles = new List<InstitutionalHolderProfile>(holders.Count);
        var evidence = new List<HolderClassificationEvidence>();
        foreach (var holder in holders)
        {
            var facts = HolderProfileBuilder.CollectEvidence(holder, sources);
            evidence.AddRange(facts);
            profiles.Add(HolderProfileBuilder.Build(holder, facts, sources, context));
        }

        await Replace(profiles, evidence, cancellationToken);
        _logger.LogInformation(
            "Holder profiles refreshed: {Typed} of {Total} typed, {Conflicting} with conflicting evidence",
            profiles.Count(profile => profile.EntityType != HolderEntityType.Unknown),
            profiles.Count,
            profiles.Count(profile => profile.HasConflictingEvidence)
        );
        return profiles.Count;
    }

    private async Task Replace(
        List<InstitutionalHolderProfile> profiles,
        List<HolderClassificationEvidence> evidence,
        CancellationToken cancellationToken
    )
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            await _dbContext
                .Set<HolderClassificationEvidence>()
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext
                .Set<InstitutionalHolderProfile>()
                .ExecuteDeleteAsync(cancellationToken);
            foreach (var batch in profiles.Chunk(WriteBatchSize))
                await Insert(batch, cancellationToken);
            foreach (var batch in evidence.Chunk(WriteBatchSize))
                await Insert(batch, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        _dbContext.ChangeTracker.Clear();
    }

    private async Task Insert<T>(T[] batch, CancellationToken cancellationToken)
        where T : class
    {
        _dbContext.AddRange(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    // The newest roster SEC published of each kind; until the ledger has one, the newest listing.
    private async Task<DateOnly?> LatestRoster(
        FormAdvRegistration registration,
        IReadOnlyCollection<FormAdvAdviser> advisers,
        CancellationToken cancellationToken
    ) =>
        await _dbContext
            .Set<FormAdvRosterImport>()
            .Where(import => import.Registration == registration)
            .MaxAsync(import => (DateOnly?)import.ReportDate, cancellationToken)
        ?? advisers
            .Where(adviser => adviser.Registration == registration)
            .Max(adviser => (DateOnly?)adviser.ReportDate);

    private async Task<List<BeneficialOwnershipEvidenceRow>> LoadBeneficialOwnership(
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var since = today.AddMonths(-HolderEntityTypeDeriver.BeneficialOwnershipLookbackMonths);
        var filings = await _dbContext
            .Set<BeneficialOwnershipFiling>()
            .AsNoTracking()
            .Where(filing => !filing.IsUnreadable && filing.FilingDate >= since)
            .Select(filing => new
            {
                filing.AccessionNumber,
                filing.FilingType,
                filing.FilingDate,
                filing.FilerCik,
                filing.FilingPersonTypes,
                filing.Rules,
                filing.UnrecognizedFilingPersonTypeCodes,
                Persons = filing
                    .ReportingPersons.OrderBy(person => person.Ordinal)
                    .Select(person => new { person.Cik, person.Types })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        return filings
            .Select(filing => new BeneficialOwnershipEvidenceRow(
                filing.AccessionNumber,
                filing.FilingType,
                filing.FilingDate,
                filing.FilerCik,
                filing.FilingPersonTypes,
                filing.Persons.Select(person => (person.Cik, person.Types)).ToList()
            )
            {
                Rules = filing.Rules,
                UnrecognizedFilingPersonTypeCodes = filing.UnrecognizedFilingPersonTypeCodes,
            })
            .ToList();
    }

    private Task<List<NCenRegistrantRow>> LoadRegistrants(
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var since = today.AddMonths(-HolderEntityTypeDeriver.NCenLookbackMonths);
        return _dbContext
            .Set<NCenRegistrantReport>()
            .AsNoTracking()
            .Where(report => report.ReportEndingPeriod >= since)
            .Select(report => new NCenRegistrantRow(
                report.RegistrantCik,
                report.AccessionNumber,
                report.ReportEndingPeriod,
                report.InvestmentCompanyType,
                report.FilingDate
            ))
            .ToListAsync(cancellationToken);
    }
}

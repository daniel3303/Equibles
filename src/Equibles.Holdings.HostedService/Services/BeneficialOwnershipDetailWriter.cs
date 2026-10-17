using System.Text.RegularExpressions;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Stores the cover-page details of a parsed Schedule 13D or 13G, replacing any earlier copy of
/// the same accession. Writes run in their own scope so they never touch a caller's change tracker.
/// </summary>
[Service]
public partial class BeneficialOwnershipDetailWriter
{
    private const int CodeListLength = 64;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BeneficialOwnershipDetailWriter> _logger;

    public BeneficialOwnershipDetailWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<BeneficialOwnershipDetailWriter> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task Write(Parsed13DGFiling filing, CancellationToken cancellationToken) =>
        Replace(ToEntity(filing), cancellationToken);

    /// <summary>Records an accession whose document EDGAR no longer serves or that cannot be parsed.</summary>
    public Task WriteUnreadable(
        string accessionNumber,
        FilingType filingType,
        DateOnly filingDate,
        string filerCik,
        CancellationToken cancellationToken
    ) =>
        Replace(
            new BeneficialOwnershipFiling
            {
                AccessionNumber = accessionNumber,
                FilingType = filingType,
                FilingDate = filingDate,
                FilerCik = filerCik?.TrimStart('0'),
                IsUnreadable = true,
            },
            cancellationToken
        );

    /// <summary>
    /// The realtime path's write: a failure is logged and left to the detail sync, so it never
    /// costs the filing's holdings import.
    /// </summary>
    public async Task TryWrite(Parsed13DGFiling filing, CancellationToken cancellationToken)
    {
        try
        {
            await Write(filing, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogWarning(
                exception,
                "Could not store 13D/13G details for {Accession}; the detail sync will retry",
                filing.AccessionNumber
            );
        }
    }

    internal static BeneficialOwnershipFiling ToEntity(Parsed13DGFiling filing)
    {
        var lead = Realtime13DGArchiveBuilder.SelectLeadPerson(filing);
        var entity = new BeneficialOwnershipFiling
        {
            AccessionNumber = filing.AccessionNumber,
            FilingType = filing.FilingType,
            IsAmendment = filing.IsAmendment,
            FilingDate = filing.FilingDate,
            DateOfEvent = filing.DateOfEvent == DateOnly.MinValue ? null : filing.DateOfEvent,
            FilerCik = Truncate(filing.FilerCik, 16),
            IssuerCik = Truncate(filing.IssuerCik, 16),
            IssuerCusip = Truncate(filing.IssuerCusip, 16),
            Rules = ParseRules(filing.RuleDesignations),
            FilingPersonTypes = ReportingPersonTypeCodes.Parse(
                filing.FilingPersonTypeCodes,
                out var unrecognizedFilingCodes
            ),
            UnrecognizedFilingPersonTypeCodes = Truncate(unrecognizedFilingCodes, CodeListLength),
        };

        for (var ordinal = 0; ordinal < filing.ReportingPersons.Count; ordinal++)
        {
            var person = filing.ReportingPersons[ordinal];
            entity.ReportingPersons.Add(
                new BeneficialOwnershipReportingPerson
                {
                    Ordinal = ordinal,
                    Cik = Truncate(person.Cik, 16),
                    Name = Truncate(person.Name, 512),
                    Types = ReportingPersonTypeCodes.Parse(person.TypeCodes, out var unrecognized),
                    UnrecognizedTypeCodes = Truncate(unrecognized, CodeListLength),
                    CitizenshipOrOrganization = Truncate(person.CitizenshipOrOrganization, 64),
                    SoleVotingPower = person.SoleVotingPower,
                    SharedVotingPower = person.SharedVotingPower,
                    SoleDispositivePower = person.SoleDispositivePower,
                    SharedDispositivePower = person.SharedDispositivePower,
                    AggregateAmountOwned = person.AggregateAmountOwned,
                    PercentOfClass = person.PercentOfClass,
                    IsLead = ReferenceEquals(person, lead),
                }
            );
        }

        return entity;
    }

    internal static Schedule13GRules ParseRules(IEnumerable<string> designations)
    {
        var rules = Schedule13GRules.None;
        foreach (var designation in designations ?? [])
        {
            foreach (Match match in RulePattern().Matches(designation ?? ""))
            {
                rules |= char.ToLowerInvariant(match.Groups[1].Value[0]) switch
                {
                    'b' => Schedule13GRules.QualifiedInstitution,
                    'c' => Schedule13GRules.PassiveInvestor,
                    _ => Schedule13GRules.ExemptInvestor,
                };
            }
        }

        return rules;
    }

    private async Task Replace(
        BeneficialOwnershipFiling entity,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                dbContext.ChangeTracker.Clear();
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    cancellationToken
                );
                // Persons go with their filing through the foreign key's ON DELETE CASCADE.
                await dbContext
                    .Set<BeneficialOwnershipFiling>()
                    .Where(row => row.AccessionNumber == entity.AccessionNumber)
                    .ExecuteDeleteAsync(cancellationToken);
                dbContext.Add(entity);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException exception)
            when (exception.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            // A concurrent writer stored the same accession first; both read the same document.
            _logger.LogDebug(
                "13D/13G details for {Accession} were stored concurrently",
                entity.AccessionNumber
            );
        }
    }

    private static string Truncate(string value, int length) =>
        string.IsNullOrEmpty(value) ? null
        : value.Length <= length ? value
        : value[..length];

    [GeneratedRegex(@"13d-1\s*\(\s*([bcd])\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex RulePattern();
}

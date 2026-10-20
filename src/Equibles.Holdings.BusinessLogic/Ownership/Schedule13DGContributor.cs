using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// The Schedule 13D and 13G lane: each filer's latest 13D or 13G for the listing by event date,
/// carrying the lead person's position the holdings row keeps, compared with the same filer's
/// previous 13D or 13G for the listing (the pair reports one beneficial-ownership measure).
/// </summary>
public class Schedule13DGContributor : IHolderSourceContributor
{
    private readonly InstitutionalHoldingRepository _holdingRepository;

    public Schedule13DGContributor(InstitutionalHoldingRepository holdingRepository)
    {
        _holdingRepository = holdingRepository;
    }

    public async Task<IReadOnlyList<StockHolderRow>> Read(
        StockOwnershipScope scope,
        CancellationToken cancellationToken = default
    )
    {
        var positions = await _holdingRepository.GetSchedule13DGByListing(
            scope.Stock,
            scope.ListedTicker,
            cancellationToken
        );
        if (positions.Count == 0)
            return [];

        // Only a 13D names its reporting persons with a CIK; a group's co-filers are its persons.
        var accessions = positions
            .Where(p => p.FilingType == FilingType.Schedule13D && p.AccessionNumber != null)
            .Select(p => p.AccessionNumber)
            .Distinct()
            .ToList();
        var persons =
            accessions.Count == 0
                ? []
                : await _holdingRepository
                    .GetBeneficialOwnershipPersons(accessions)
                    .Where(person => person.Cik != null)
                    .AsNoTracking()
                    .Select(person => new { person.Filing.AccessionNumber, person.Cik })
                    .ToListAsync(cancellationToken);
        var personsByAccession = persons
            .GroupBy(person => person.AccessionNumber)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(person => CikNormalizer.Canonicalize(person.Cik))
                        .OfType<string>()
                        .Distinct()
                        .ToList()
            );

        return positions
            .Select(p =>
            {
                var cik = CikNormalizer.Canonicalize(p.HolderCik);
                var shares = scope.Restate(p.Shares, p.EventDate, p.ListedTicker);
                long? prior =
                    p.PriorShares is { } priorShares && p.PriorEventDate is { } priorDate
                        ? scope.Restate(priorShares, priorDate, p.ListedTicker)
                        : null;
                return new StockHolderRow
                {
                    HolderKey = new StockHolderKey(
                        cik == null ? StockHolderKeyKind.Opaque : StockHolderKeyKind.Cik,
                        cik ?? p.InstitutionalHolderId.ToString()
                    ),
                    Source =
                        p.FilingType == FilingType.Schedule13D
                            ? StockHolderSource.Schedule13D
                            : StockHolderSource.Schedule13G,
                    Name = p.HolderName,
                    Cik = cik,
                    InstitutionalHolderId = p.InstitutionalHolderId,
                    Detail = RuleLabel(p.Rules),
                    ReportingPersonCiks =
                        p.AccessionNumber != null
                            ? personsByAccession.GetValueOrDefault(p.AccessionNumber) ?? []
                            : [],
                    Shares = shares,
                    FiledShares = p.Shares,
                    ChangeShares = prior is { } before ? shares - before : null,
                    PriorAsOf = prior == null ? null : p.PriorEventDate,
                    FiledPercentOfClass = p.PercentOfClass,
                    AsOf = p.EventDate,
                    FilingDate = p.FilingDate,
                    Accession = p.AccessionNumber,
                    Country = p.HolderStateOrCountry,
                    Flags = scope.SplitUnresolved(p.EventDate, p.ListedTicker)
                        ? StockHolderRowFlags.SplitUnresolved
                        : StockHolderRowFlags.None,
                };
            })
            .ToList();
    }

    private static string RuleLabel(Schedule13GRules rules) =>
        rules.HasFlag(Schedule13GRules.QualifiedInstitution)
            ? "Rule 13d-1(b), qualified institution"
        : rules.HasFlag(Schedule13GRules.PassiveInvestor) ? "Rule 13d-1(c), passive investor"
        : rules.HasFlag(Schedule13GRules.ExemptInvestor) ? "Rule 13d-1(d), exempt investor"
        : null;
}

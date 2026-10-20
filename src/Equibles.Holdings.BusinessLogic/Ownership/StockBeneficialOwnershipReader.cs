using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// A listing's Schedule 13D and 13G record: each filer's latest filing with every reporting person
/// it names, largest block first.
/// </summary>
public class StockBeneficialOwnershipReader
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IBeneficialOwnershipPurposeSource _purposeSource;

    public StockBeneficialOwnershipReader(
        InstitutionalHoldingRepository holdingRepository,
        StockSplitRepository stockSplitRepository,
        TimeProvider timeProvider = null,
        IBeneficialOwnershipPurposeSource purposeSource = null
    )
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _purposeSource = purposeSource;
        _holdingRepository = holdingRepository;
        _stockSplitRepository = stockSplitRepository;
    }

    public async Task<IReadOnlyList<BeneficialOwnershipBlock>> Read(
        EquityIssuer stock,
        string requestedTicker,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var listedTicker =
            SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker)
            ?? stock.Presentation.Listing.Ticker;
        var positions = await _holdingRepository.GetSchedule13DGByListing(
            stock,
            listedTicker,
            cancellationToken
        );
        if (positions.Count == 0)
            return [];
        var scope = new StockOwnershipScope
        {
            Stock = stock,
            ListedTicker = listedTicker,
            ExactListing = SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker),
            Splits = await _stockSplitRepository
                .GetEffectiveByStock(stock.Id, today)
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            Today = today,
        };

        var accessions = positions
            .Where(p => p.AccessionNumber != null)
            .Select(p => p.AccessionNumber)
            .Distinct()
            .ToList();
        var persons = await _holdingRepository
            .GetBeneficialOwnershipPersons(accessions)
            .AsNoTracking()
            .Select(person => new
            {
                person.Filing.AccessionNumber,
                person.Ordinal,
                person.Name,
                person.Cik,
                person.Types,
                person.CitizenshipOrOrganization,
                person.AggregateAmountOwned,
                person.PercentOfClass,
                person.SoleVotingPower,
                person.SharedVotingPower,
                person.SoleDispositivePower,
                person.SharedDispositivePower,
                person.IsLead,
            })
            .ToListAsync(cancellationToken);
        var personsByAccession = persons
            .GroupBy(person => person.AccessionNumber)
            .ToDictionary(group => group.Key, group => group.OrderBy(p => p.Ordinal).ToList());

        var blocks = positions
            .Select(p =>
            {
                long Restate(long shares) => scope.Restate(shares, p.EventDate, p.ListedTicker);
                long? prior =
                    p.PriorShares is { } priorShares && p.PriorEventDate is { } priorDate
                        ? scope.Restate(priorShares, priorDate, p.ListedTicker)
                        : null;
                var shares = Restate(p.Shares);
                return new BeneficialOwnershipBlock
                {
                    FilerName = p.HolderName,
                    FilerCik = CikNormalizer.Canonicalize(p.HolderCik),
                    FilingType = p.FilingType,
                    IsAmendment = p.IsAmendment,
                    EventDate = p.EventDate,
                    FilingDate = p.FilingDate,
                    AccessionNumber = p.AccessionNumber,
                    Rules = p.Rules,
                    Shares = shares,
                    FiledShares = p.Shares,
                    PercentOfClass = p.PercentOfClass,
                    ChangeShares = prior is { } before ? shares - before : null,
                    PriorEventDate = prior == null ? null : p.PriorEventDate,
                    SplitUnresolved = scope.SplitUnresolved(p.EventDate, p.ListedTicker),
                    HasCoverPage = p.HasCoverPage,
                    Persons = (
                        p.AccessionNumber != null
                            ? personsByAccession.GetValueOrDefault(p.AccessionNumber) ?? []
                            : []
                    )
                        .Select(person => new BeneficialOwnershipPerson(
                            person.Name,
                            CikNormalizer.Canonicalize(person.Cik),
                            person.Types,
                            person.CitizenshipOrOrganization,
                            Restate(person.AggregateAmountOwned),
                            person.PercentOfClass,
                            Restate(person.SoleVotingPower),
                            Restate(person.SharedVotingPower),
                            Restate(person.SoleDispositivePower),
                            Restate(person.SharedDispositivePower),
                            person.IsLead
                        ))
                        .ToList(),
                };
            })
            .OrderByDescending(block => block.Shares)
            .ThenByDescending(block => block.EventDate)
            .ThenBy(block => block.FilerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(block => block.AccessionNumber, StringComparer.Ordinal)
            .ToList();
        await StockOwnershipReader.DescribePurposes(
            blocks.Where(block => block.FilingType == FilingType.Schedule13D).ToList(),
            block => block.AccessionNumber,
            (block, purpose) => block.Purpose = purpose,
            _purposeSource,
            cancellationToken
        );
        return blocks;
    }
}

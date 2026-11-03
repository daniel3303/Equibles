using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// One filer's Schedule 13D and 13G stakes: its latest filing on each listed security it ever
/// disclosed, with the status <see cref="Schedule13DGStatus"/> gives it. Stakes still held come
/// first, newest event first; exited, below-threshold and retired ones follow for callers that
/// show history.
/// </summary>
public class HolderBeneficialOwnershipReader
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly EquityIssuerRepository _issuerRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IBeneficialOwnershipPurposeSource _purposeSource;

    public HolderBeneficialOwnershipReader(
        InstitutionalHoldingRepository holdingRepository,
        EquityIssuerRepository issuerRepository,
        StockSplitRepository stockSplitRepository,
        TimeProvider timeProvider = null,
        IBeneficialOwnershipPurposeSource purposeSource = null
    )
    {
        _holdingRepository = holdingRepository;
        _issuerRepository = issuerRepository;
        _stockSplitRepository = stockSplitRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _purposeSource = purposeSource;
    }

    public async Task<IReadOnlyList<HolderBeneficialOwnershipStake>> Read(
        InstitutionalHolder holder,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var positions = await _holdingRepository.GetSchedule13DGByHolder(holder, cancellationToken);
        if (positions.Count == 0)
            return [];

        // The unfiltered issuer read keeps merged and delisted issuers: a SPAC stake stays a stake.
        // An index manager holds stakes in thousands of issuers, so the securities and listings
        // load as split queries rather than one cartesian join.
        var issuerIds = positions.Select(p => p.EquityIssuerId).Distinct().ToList();
        var issuers = await _issuerRepository
            .GetAll()
            .Where(issuer => issuerIds.Contains(issuer.Id))
            .AsNoTracking()
            .AsSplitQuery()
            .ToDictionaryAsync(issuer => issuer.Id, cancellationToken);
        var splitsByIssuer = (
            await _stockSplitRepository
                .GetEffective(today)
                .Where(split => issuerIds.Contains(split.EquityIssuerId))
                .AsNoTracking()
                .ToListAsync(cancellationToken)
        ).ToLookup(split => split.EquityIssuerId);
        var scopes = new Dictionary<Guid, StockOwnershipScope>();
        foreach (var (id, issuer) in issuers)
        {
            // A folded or retired issuer may have no presentation line; its counts stay as filed.
            if (issuer.Presentation?.Listing == null)
                continue;
            scopes[id] = new StockOwnershipScope
            {
                Stock = issuer,
                ListedTicker = issuer.Presentation.Listing.Ticker,
                Splits = splitsByIssuer[id].ToList(),
                Today = today,
            };
        }

        var stakes = positions
            .Select(p =>
            {
                var scope = scopes.GetValueOrDefault(p.EquityIssuerId);
                var issuer = issuers.GetValueOrDefault(p.EquityIssuerId);
                long Restate(long count, DateOnly asOf) =>
                    scope?.Restate(count, asOf, p.ListedTicker) ?? count;
                var shares = Restate(p.Shares, p.EventDate);
                long? prior =
                    p.PriorShares is { } priorShares && p.PriorEventDate is { } priorDate
                        ? Restate(priorShares, priorDate)
                        : null;
                return new HolderBeneficialOwnershipStake
                {
                    EquityIssuerId = p.EquityIssuerId,
                    IssuerName = p.IssuerName,
                    Ticker = p.ListedTicker ?? p.PresentationTicker,
                    CurrentTicker = p.PresentationTicker,
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
                    SplitUnresolved = scope?.SplitUnresolved(p.EventDate, p.ListedTicker) ?? false,
                    HasCoverPage = p.HasCoverPage,
                    Status = Schedule13DGStatus.Of(
                        shares,
                        p.PercentOfClass,
                        p.EventDate,
                        today,
                        Schedule13DGStatus.IsListed(issuer)
                    ),
                    DelistedOn = p.DelistedOn,
                };
            })
            .OrderBy(stake => Schedule13DGStatus.IsHeld(stake.Status) ? 0 : 1)
            .ThenByDescending(stake => stake.EventDate)
            .ThenBy(stake => stake.Ticker, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stake => stake.AccessionNumber, StringComparer.Ordinal)
            .ToList();
        await StockOwnershipReader.ReadPurposes(
            stakes.Where(stake => stake.FilingType == FilingType.Schedule13D).ToList(),
            stake => stake.AccessionNumber,
            (stake, purpose) => stake.Purpose = purpose,
            _purposeSource,
            cancellationToken
        );
        return stakes;
    }
}

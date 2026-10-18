using System.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Resolves reviewed BioGuide-backed aliases, writing every affected member, filing, trade, and
/// redirect in one transaction, and otherwise assigns an id only when a seat or Senate term
/// covering the member's filing dates and an exact surname point at one legislator. Unknown
/// names retain their own row; identity is never inferred from initials or name similarity, and
/// rows are merged only by the reviewed catalog.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(ICongressMemberIdentityService))]
public class CongressMemberIdentityService : ICongressMemberIdentityService
{
    private const long ReconciliationLockId = 4305;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CongressMemberHistoryMerger _historyMerger;
    private readonly ILogger<CongressMemberIdentityService> _logger;

    public CongressMemberIdentityService(
        EquiblesFinancialDbContext dbContext,
        ILogger<CongressMemberIdentityService> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
        _historyMerger = new CongressMemberHistoryMerger(dbContext, logger);
    }

    public async Task ReconcileMembers(CancellationToken ct)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct
        );
        await AcquireReconciliationLock(ct);
        await ReconcileCatalog(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<LegislatorIdentityReconciliation> ReconcileLegislatorIdentities(
        CancellationToken ct
    )
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct
        );
        await AcquireReconciliationLock(ct);
        await ReconcileCatalog(ct);
        var result = await MatchLegislators(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<Dictionary<string, CongressMember>> UpsertMembers(
        IReadOnlyCollection<CongressMemberObservation> observations,
        CancellationToken ct
    )
    {
        if (observations.Count == 0)
            return new Dictionary<string, CongressMember>(StringComparer.OrdinalIgnoreCase);

        var normalized = observations.Select(ResolveObservation).ToList();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct
        );
        await AcquireReconciliationLock(ct);

        await ReconcileCatalog(ct);

        var members = BuildMembers(normalized);
        await UpsertMemberRows(members, ct);
        var persisted = await LoadPersistedMembers(members, ct);

        await transaction.CommitAsync(ct);

        return MapFilingNames(normalized, persisted);
    }

    private static ResolvedCongressMemberObservation ResolveObservation(
        CongressMemberObservation observation
    )
    {
        var filingName = DisclosureParsingHelper.NormalizeMemberName(observation.FilingName);
        var identity = CongressMemberIdentityCatalog.Resolve(filingName);
        return new ResolvedCongressMemberObservation(
            filingName,
            identity?.CanonicalName ?? filingName,
            identity?.BioguideId,
            observation.Position,
            observation.StateDistrict,
            observation.ObservedAt
        );
    }

    private static List<CongressMember> BuildMembers(
        IEnumerable<ResolvedCongressMemberObservation> observations
    ) =>
        observations
            .GroupBy(observation => observation.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latest = group.OrderBy(observation => observation.ObservedAt).Last();
                var latestSeat = group
                    .Where(observation => !string.IsNullOrWhiteSpace(observation.StateDistrict))
                    .OrderBy(observation => observation.ObservedAt)
                    .LastOrDefault()
                    ?.StateDistrict.Trim();
                var bioguideId = group
                    .Select(observation => observation.BioguideId)
                    .FirstOrDefault(id => id != null);
                return new CongressMember
                {
                    Name = group.Key,
                    BioguideId = bioguideId,
                    BioguideIdSource = bioguideId == null ? null : CongressBioguideIdSource.Catalog,
                    Position = latest.Position,
                    StateDistrict = latestSeat,
                };
            })
            .ToList();

    private async Task UpsertMemberRows(
        IReadOnlyCollection<CongressMember> members,
        CancellationToken ct
    )
    {
        // A matched catalog row already carries its id and source from ReconcileCatalog, so the
        // source is written only on insert.
        await _dbContext
            .Set<CongressMember>()
            .UpsertRange(members)
            .On(member => new { member.Name })
            .WhenMatched(
                (existing, incoming) =>
                    new CongressMember
                    {
                        BioguideId = incoming.BioguideId ?? existing.BioguideId,
                        Position = incoming.Position,
                        StateDistrict = incoming.StateDistrict ?? existing.StateDistrict,
                    }
            )
            .RunAsync(ct);
    }

    private async Task<Dictionary<string, CongressMember>> LoadPersistedMembers(
        IEnumerable<CongressMember> members,
        CancellationToken ct
    )
    {
        var canonicalNames = members.Select(member => member.Name).ToList();
        return await _dbContext
            .Set<CongressMember>()
            .Where(member => canonicalNames.Contains(member.Name))
            .ToDictionaryAsync(member => member.Name, StringComparer.OrdinalIgnoreCase, ct);
    }

    private static Dictionary<string, CongressMember> MapFilingNames(
        IEnumerable<ResolvedCongressMemberObservation> normalized,
        IReadOnlyDictionary<string, CongressMember> persisted
    ) =>
        normalized
            .GroupBy(observation => observation.FilingName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => persisted[group.First().CanonicalName],
                StringComparer.OrdinalIgnoreCase
            );

    private Task<int> AcquireReconciliationLock(CancellationToken ct) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({ReconciliationLockId})",
            cancellationToken: ct
        );

    private async Task ReconcileCatalog(CancellationToken ct)
    {
        foreach (var identity in CongressMemberIdentityCatalog.All)
        {
            var aliases = identity.Aliases.Select(alias => alias.ToLowerInvariant()).ToList();
            var candidates = await _dbContext
                .Set<CongressMember>()
                .Where(member =>
                    member.BioguideId == identity.BioguideId
                    || aliases.Contains(member.Name.ToLower())
                )
                .OrderBy(member => member.CreationTime)
                .ThenBy(member => member.Id)
                .ToListAsync(ct);
            if (candidates.Count == 0)
                continue;

            var conflicting = candidates
                .Where(member =>
                    member.BioguideId != null && member.BioguideId != identity.BioguideId
                )
                .ToList();
            var reviewedConflict = conflicting.FirstOrDefault(member =>
                member.BioguideIdSource != CongressBioguideIdSource.SeatAndSurnameMatch
            );
            if (reviewedConflict != null)
            {
                throw new InvalidOperationException(
                    $"Congress member '{reviewedConflict.Name}' is already assigned BioGuide id "
                        + $"'{reviewedConflict.BioguideId}', not '{identity.BioguideId}'"
                );
            }

            // A reviewed alias outranks a seat-and-surname match, so the matched id is replaced.
            foreach (var matched in conflicting)
            {
                _logger.LogWarning(
                    "Catalog BioGuide id {CatalogId} replaces matched id {MatchedId} on congress member {Name}",
                    identity.BioguideId,
                    matched.BioguideId,
                    matched.Name
                );
                matched.BioguideId = null;
                matched.BioguideIdSource = null;
            }

            var survivor =
                candidates.FirstOrDefault(member =>
                    string.Equals(
                        member.Name,
                        identity.CanonicalName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                ?? candidates.FirstOrDefault(member => member.BioguideId == identity.BioguideId)
                ?? candidates[0];
            var retired = candidates.Where(member => member.Id != survivor.Id).ToList();

            if (retired.Count > 0)
                await _historyMerger.MergeMembers(survivor, retired, ct);

            survivor.Name = identity.CanonicalName;
            survivor.BioguideId = identity.BioguideId;
            survivor.BioguideIdSource = CongressBioguideIdSource.Catalog;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task<LegislatorIdentityReconciliation> MatchLegislators(CancellationToken ct)
    {
        var aliasNames = CongressMemberIdentityCatalog
            .All.SelectMany(identity => identity.Aliases)
            .Select(alias => alias.ToLowerInvariant())
            .Distinct()
            .ToList();
        var members = await _dbContext
            .Set<CongressMember>()
            .Where(member => !aliasNames.Contains(member.Name.ToLower()))
            .ToListAsync(ct);
        var filingDates = await LoadFilingDates(ct);
        var terms = await _dbContext
            .Set<CongressMemberTerm>()
            .Select(term => new LegislatorTermCandidate(
                term.BioguideId,
                term.Legislator.LastName,
                term.Chamber,
                term.State,
                term.District,
                term.StartDate,
                term.EndDate
            ))
            .ToListAsync(ct);
        var holders = await _dbContext
            .Set<CongressMember>()
            .Where(member => member.BioguideId != null)
            .Select(member => new { member.Id, member.BioguideId })
            .ToDictionaryAsync(member => member.BioguideId, member => member.Id, ct);

        var matcher = new CongressMemberLegislatorMatcher(terms);
        var derived = members
            .Select(member =>
                (
                    Member: member,
                    BioguideId: matcher.Match(
                        new CongressMemberMatchSubject(
                            member.Name,
                            member.Position,
                            member.StateDistrict,
                            filingDates[member.Id].ToList()
                        )
                    )
                )
            )
            .Where(pair => pair.BioguideId != null)
            .ToList();

        var result = new LegislatorIdentityReconciliation();
        foreach (var (member, bioguideId) in derived.Where(pair => pair.Member.BioguideId != null))
        {
            if (member.BioguideId != bioguideId)
                AddConflict(result, member, bioguideId, "the member already has another id");
        }

        foreach (
            var group in derived
                .Where(pair => pair.Member.BioguideId == null)
                .GroupBy(pair => pair.BioguideId, StringComparer.Ordinal)
        )
        {
            var rows = group.Select(pair => pair.Member).ToList();
            if (rows.Count > 1)
            {
                foreach (var member in rows)
                    AddConflict(result, member, group.Key, "several unresolved members match it");
                continue;
            }

            if (holders.ContainsKey(group.Key))
            {
                AddConflict(result, rows[0], group.Key, "another member already has it");
                continue;
            }

            rows[0].BioguideId = group.Key;
            rows[0].BioguideIdSource = CongressBioguideIdSource.SeatAndSurnameMatch;
            result.Assigned++;
        }

        await _dbContext.SaveChangesAsync(ct);
        return result;
    }

    // Every distinct filing date per member across trades and annual reports, read in one query.
    private async Task<ILookup<Guid, DateOnly>> LoadFilingDates(CancellationToken ct)
    {
        var tradeDates = _dbContext
            .Set<CongressionalTrade>()
            .Select(trade => new { trade.CongressMemberId, Date = trade.FilingDate });
        var annualDates = _dbContext
            .Set<CongressionalAnnualDisclosure>()
            .Select(report => new { report.CongressMemberId, Date = report.FiledDate });
        var rows = await tradeDates.Union(annualDates).ToListAsync(ct);
        return rows.ToLookup(row => row.CongressMemberId, row => row.Date);
    }

    private void AddConflict(
        LegislatorIdentityReconciliation result,
        CongressMember member,
        string derivedBioguideId,
        string reason
    )
    {
        _logger.LogWarning(
            "Congress member {Name} matches legislator {Derived} by seat and surname but was not assigned: {Reason}",
            member.Name,
            derivedBioguideId,
            reason
        );
        result.Conflicts.Add(
            new LegislatorIdentityConflict(
                member.Name,
                member.BioguideId,
                derivedBioguideId,
                reason
            )
        );
    }
}

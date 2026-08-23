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
/// Resolves only reviewed BioGuide-backed aliases, then writes every affected member, filing,
/// trade, and redirect in one transaction. Unknown names retain their own row; this service
/// never infers identity from initials, punctuation, district, or name similarity.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(ICongressMemberIdentityService))]
public class CongressMemberIdentityService : ICongressMemberIdentityService
{
    private const long ReconciliationLockId = 4305;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CongressMemberHistoryMerger _historyMerger;

    public CongressMemberIdentityService(
        EquiblesFinancialDbContext dbContext,
        ILogger<CongressMemberIdentityService> logger
    )
    {
        _dbContext = dbContext;
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
                return new CongressMember
                {
                    Name = group.Key,
                    BioguideId = group
                        .Select(observation => observation.BioguideId)
                        .FirstOrDefault(id => id != null),
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

            var conflicting = candidates.FirstOrDefault(member =>
                member.BioguideId != null && member.BioguideId != identity.BioguideId
            );
            if (conflicting != null)
            {
                throw new InvalidOperationException(
                    $"Congress member '{conflicting.Name}' is already assigned BioGuide id "
                        + $"'{conflicting.BioguideId}', not '{identity.BioguideId}'"
                );
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
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}

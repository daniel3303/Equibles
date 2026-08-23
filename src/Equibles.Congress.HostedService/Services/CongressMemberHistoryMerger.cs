using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

internal sealed class CongressMemberHistoryMerger
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<CongressMemberIdentityService> _logger;

    public CongressMemberHistoryMerger(
        EquiblesFinancialDbContext dbContext,
        ILogger<CongressMemberIdentityService> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task MergeMembers(
        CongressMember survivor,
        IReadOnlyCollection<CongressMember> retired,
        CancellationToken ct
    )
    {
        PreserveStateDistrict(survivor, retired);
        var memberIds = retired.Select(member => member.Id).Append(survivor.Id).ToList();
        var (reassignedTrades, duplicateTrades) = await MergeTrades(survivor.Id, memberIds, ct);
        var (reassignedDisclosures, duplicateDisclosures) = await MergeDisclosures(
            survivor.Id,
            memberIds,
            ct
        );
        await RewriteRedirectsAndRemoveMembers(survivor.Id, retired, ct);

        _logger.LogInformation(
            "Merged {RetiredCount} duplicate Congress members into {MemberId}; reassigned "
                + "{TradeCount} trades and {DisclosureCount} annual disclosures, removed "
                + "{DuplicateTradeCount} duplicate trades and {DuplicateDisclosureCount} superseded disclosures",
            retired.Count,
            survivor.Id,
            reassignedTrades,
            reassignedDisclosures,
            duplicateTrades,
            duplicateDisclosures
        );
    }

    private static void PreserveStateDistrict(
        CongressMember survivor,
        IEnumerable<CongressMember> retired
    )
    {
        survivor.StateDistrict ??= retired
            .Where(member => !string.IsNullOrWhiteSpace(member.StateDistrict))
            .OrderBy(member => member.CreationTime)
            .LastOrDefault()
            ?.StateDistrict;
    }

    private async Task<(int Reassigned, int Duplicates)> MergeTrades(
        Guid survivorId,
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken ct
    )
    {
        var trades = await _dbContext
            .Set<CongressionalTrade>()
            .Where(trade => memberIds.Contains(trade.CongressMemberId))
            .OrderByDescending(trade => trade.CongressMemberId == survivorId)
            .ThenBy(trade => trade.CreationTime)
            .ThenBy(trade => trade.Id)
            .ToListAsync(ct);
        var retainedKeys = new HashSet<CongressionalTradeIdentity>();
        var duplicates = new List<CongressionalTrade>();
        var reassigned = new List<CongressionalTrade>();
        foreach (var trade in trades)
        {
            if (!retainedKeys.Add(CongressionalTradeIdentity.From(trade)))
            {
                duplicates.Add(trade);
                continue;
            }

            if (trade.CongressMemberId != survivorId)
                reassigned.Add(trade);
        }

        _dbContext.RemoveRange(duplicates);
        await _dbContext.SaveChangesAsync(ct);
        foreach (var trade in reassigned.Except(duplicates))
            trade.CongressMemberId = survivorId;
        await _dbContext.SaveChangesAsync(ct);
        return (reassigned.Count, duplicates.Count);
    }

    private async Task<(int Reassigned, int Duplicates)> MergeDisclosures(
        Guid survivorId,
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken ct
    )
    {
        var disclosures = await _dbContext
            .Set<CongressionalAnnualDisclosure>()
            .Where(disclosure => memberIds.Contains(disclosure.CongressMemberId))
            .ToListAsync(ct);
        var retained = disclosures
            .GroupBy(disclosure => disclosure.Year)
            .Select(group => SelectLatestDisclosure(group, survivorId))
            .ToHashSet();
        var duplicates = disclosures.Where(disclosure => !retained.Contains(disclosure)).ToList();
        var reassigned = retained
            .Where(disclosure => disclosure.CongressMemberId != survivorId)
            .ToList();

        _dbContext.RemoveRange(duplicates);
        await _dbContext.SaveChangesAsync(ct);
        foreach (var disclosure in reassigned)
            disclosure.CongressMemberId = survivorId;
        await _dbContext.SaveChangesAsync(ct);
        return (reassigned.Count, duplicates.Count);
    }

    private static CongressionalAnnualDisclosure SelectLatestDisclosure(
        IEnumerable<CongressionalAnnualDisclosure> disclosures,
        Guid survivorId
    ) =>
        disclosures
            .OrderByDescending(disclosure => disclosure.FiledDate)
            .ThenByDescending(disclosure => disclosure.CreationTime)
            .ThenByDescending(disclosure => disclosure.CongressMemberId == survivorId)
            .ThenBy(disclosure => disclosure.Id)
            .First();

    private async Task RewriteRedirectsAndRemoveMembers(
        Guid survivorId,
        IReadOnlyCollection<CongressMember> retired,
        CancellationToken ct
    )
    {
        var retiredIds = retired.Select(member => member.Id).ToList();
        var redirects = await _dbContext
            .Set<CongressMemberRedirect>()
            .Where(redirect =>
                retiredIds.Contains(redirect.Id) || retiredIds.Contains(redirect.MergedIntoId)
            )
            .ToListAsync(ct);
        foreach (
            var redirect in redirects.Where(redirect => retiredIds.Contains(redirect.MergedIntoId))
        )
            redirect.MergedIntoId = survivorId;
        foreach (var retiredMember in retired)
        {
            var redirect = redirects.FirstOrDefault(existing => existing.Id == retiredMember.Id);
            if (redirect == null)
            {
                _dbContext.Add(
                    new CongressMemberRedirect { Id = retiredMember.Id, MergedIntoId = survivorId }
                );
            }
            else
            {
                redirect.MergedIntoId = survivorId;
            }
        }

        _dbContext.RemoveRange(retired);
        await _dbContext.SaveChangesAsync(ct);
    }
}

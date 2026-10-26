using Equibles.Congress.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Deletes rows stored before stable source identity once a replay of the same filing stored the
/// same trade under its source key with the same or more filed metadata. Those filings are already
/// recorded at the current parser version, so no replay-time repair would revisit them.
/// </summary>
[Service]
public class CongressionalLegacyTradeRetirement
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<CongressionalLegacyTradeRetirement> _logger;

    public CongressionalLegacyTradeRetirement(
        EquiblesFinancialDbContext dbContext,
        ILogger<CongressionalLegacyTradeRetirement> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public virtual async Task<int> RetireSuperseded(CancellationToken ct)
    {
        var trades = _dbContext.Set<CongressionalTrade>();
        // Issuer identity is not part of the match: the replay derives it from dated SEC evidence,
        // and the legacy link is exactly what that evidence replaces.
        var pairs = await (
            from legacy in trades
            where legacy.SourceId == null
            join source in trades.Where(trade => trade.SourceId != null)
                on new
                {
                    legacy.CongressMemberId,
                    legacy.FilingDate,
                    legacy.TransactionDate,
                    legacy.TransactionType,
                    legacy.OwnerType,
                    legacy.AmountFrom,
                    legacy.AmountTo,
                } equals new
                {
                    source.CongressMemberId,
                    source.FilingDate,
                    source.TransactionDate,
                    source.TransactionType,
                    source.OwnerType,
                    source.AmountFrom,
                    source.AmountTo,
                }
            select new
            {
                Legacy = legacy,
                Source = new SourceRow(
                    source.Id,
                    source.SourceId,
                    source.SourceRowIndex,
                    source.AssetName,
                    source.AssetType,
                    source.Subholding
                ),
            }
        ).ToListAsync(ct);

        var candidates = pairs
            .GroupBy(pair => pair.Legacy)
            .Select(group =>
            {
                var facts = LegacyFiledFacts.Read(group.Key);
                return facts == null
                    ? null
                    : new Candidate(
                        group.Key,
                        facts,
                        group.Select(pair => pair.Source).Where(facts.IsExtendedBy).ToList()
                    );
            })
            .Where(candidate => candidate is { Sources.Count: > 0 })
            .ToList();
        // A trade's legacy rows retire together or not at all: retiring some would let the next
        // run re-pair the survivors with replay rows already spent on the retired ones.
        var retired = candidates
            .GroupBy(candidate =>
                (
                    candidate.Legacy.CongressMemberId,
                    candidate.Legacy.FilingDate,
                    candidate.Legacy.TransactionDate,
                    candidate.Legacy.TransactionType,
                    candidate.Legacy.OwnerType,
                    candidate.Legacy.AmountFrom,
                    candidate.Legacy.AmountTo,
                    candidate.Facts.AssetName
                )
            )
            .Where(trade => PairsEveryRow(trade.ToList()))
            .SelectMany(trade => trade.Select(candidate => candidate.Legacy))
            .ToList();

        if (retired.Count == 0)
            return 0;

        _dbContext.RemoveRange(retired);
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Retired {Count} legacy congressional trades superseded by source-keyed replay rows",
            retired.Count
        );
        return retired.Count;
    }

    // Most specific legacy rows claim first, so a bare row cannot take the only replay row a
    // detailed one matches; each replay row stands in for at most one legacy row. The sequence is
    // lazy, so each pick sees the claims made before it.
    private static bool PairsEveryRow(List<Candidate> candidates)
    {
        var claimed = new HashSet<Guid>();
        return candidates
            .OrderByDescending(candidate => candidate.Facts.Specificity)
            .ThenBy(candidate => candidate.Legacy.Id)
            .Select(candidate =>
                candidate
                    .Sources.Where(source => !claimed.Contains(source.Id))
                    .OrderByDescending(source => candidate.Facts.Matches(source))
                    .ThenBy(source => source.SourceId, StringComparer.Ordinal)
                    .ThenBy(source => source.SourceRowIndex)
                    .FirstOrDefault()
            )
            .All(replacement => replacement != null && claimed.Add(replacement.Id));
    }

    private sealed record Candidate(
        CongressionalTrade Legacy,
        LegacyFiledFacts Facts,
        List<SourceRow> Sources
    );

    private sealed record SourceRow(
        Guid Id,
        string SourceId,
        int? SourceRowIndex,
        string AssetName,
        string AssetType,
        string Subholding
    );

    private sealed record LegacyFiledFacts(string AssetName, string AssetType, string Subholding)
    {
        public int Specificity => (AssetType == "" ? 0 : 1) + (Subholding == "" ? 0 : 1);

        // Older parsers left metadata inside AssetName or never read it, so the row is compared
        // with the name and subholding the current writer would have stored.
        public static LegacyFiledFacts Read(CongressionalTrade legacy)
        {
            var details = HouseDisclosureClient.ExtractInlineAssetDetails(legacy.AssetName);
            var inlineSubholding =
                details.Subholding == null
                    ? ""
                    : CongressionalTradeSyncService.CleanStoredText(
                        DisclosureParsingHelper.Truncate(details.Subholding, 256)
                    );
            if (
                legacy.Subholding != ""
                && inlineSubholding != ""
                && legacy.Subholding != inlineSubholding
            )
                return null;

            return new LegacyFiledFacts(
                DisclosureParsingHelper.CleanAssetName(
                    CongressionalTradeSyncService.CleanStoredText(details.AssetName)
                ),
                legacy.AssetType,
                legacy.Subholding != "" ? legacy.Subholding : inlineSubholding
            );
        }

        public bool IsExtendedBy(SourceRow source) =>
            source.AssetName == AssetName
            && (AssetType == "" || AssetType == source.AssetType)
            && (Subholding == "" || Subholding == source.Subholding);

        public bool Matches(SourceRow source) =>
            source.AssetName == AssetName
            && source.AssetType == AssetType
            && source.Subholding == Subholding;
    }
}

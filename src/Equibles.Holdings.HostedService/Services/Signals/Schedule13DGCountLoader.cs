using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services.Signals;

/// <summary>
/// The Schedule 13D and 13G filers on record for each listed security at a quarter end: each
/// filer's newest stake on the security dated by then (event date, then filing date) counts when it
/// reports 5% of the class or more. A 13G's Rule 13d-1 paragraph comes from its cover page where
/// that was read. These rows are single stakes, never a 13F book, so this is the one holder-signals
/// load of the 13D and 13G pair.
/// </summary>
internal static class Schedule13DGCountLoader
{
    /// <summary>A stake below this percent of the class is an exit filing.</summary>
    public const decimal ReportingThresholdPercent = 5m;

    private const int ChunkSize = 5_000;

    public static async Task<Dictionary<HolderSecurityKey, Schedule13DGCounts>> Load(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var stakes = await dbContext
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Where(holding =>
                (
                    holding.FilingType == FilingType.Schedule13D
                    || holding.FilingType == FilingType.Schedule13G
                )
                && holding.ReportDate <= reportDate
            )
            .Select(holding => new Stake
            {
                Id = holding.Id,
                InstitutionalHolderId = holding.InstitutionalHolderId,
                EquityIssuerId = holding.EquityIssuerId,
                ListedTicker = holding.ListedTicker,
                EventDate = holding.ReportDate,
                FilingDate = holding.FilingDate,
                AccessionNumber = holding.AccessionNumber,
                FilingType = holding.FilingType,
                Shares = holding.Shares,
                PercentOfClass = holding.PercentOfClass,
            })
            .ToListAsync(cancellationToken);
        if (stakes.Count == 0)
            return [];

        // A stake filed under the presentation ticker is the primary listing, as 13F rows store it.
        var issuerIds = stakes.Select(stake => stake.EquityIssuerId).Distinct().ToList();
        var primaryTickers = new Dictionary<Guid, string>();
        foreach (var chunk in issuerIds.Chunk(ChunkSize))
        {
            var tickers = await dbContext
                .Set<EquityIssuer>()
                .AsNoTracking()
                .Where(issuer => chunk.Contains(issuer.Id))
                .Select(issuer => new { issuer.Id, issuer.Presentation.Listing.Ticker })
                .ToListAsync(cancellationToken);
            foreach (var issuer in tickers)
                primaryTickers[issuer.Id] = issuer.Ticker;
        }

        var standing = stakes
            .GroupBy(stake =>
                (
                    stake.InstitutionalHolderId,
                    Security: Key(stake, primaryTickers.GetValueOrDefault(stake.EquityIssuerId))
                )
            )
            .Select(group =>
                group
                    .OrderByDescending(stake => stake.EventDate)
                    .ThenByDescending(stake => stake.FilingDate)
                    .ThenByDescending(stake => stake.AccessionNumber, StringComparer.Ordinal)
                    .ThenByDescending(stake => stake.Id)
                    .First()
            )
            .Where(stake =>
                stake.Shares > 0
                && (
                    stake.PercentOfClass == null
                    || stake.PercentOfClass >= ReportingThresholdPercent
                )
            )
            .ToList();

        var accessions = standing
            .Where(stake => stake.FilingType == FilingType.Schedule13G)
            .Select(stake => stake.AccessionNumber)
            .Where(accession => accession != null)
            .Distinct()
            .ToList();
        var rules = new Dictionary<string, Schedule13GRules>(StringComparer.Ordinal);
        foreach (var chunk in accessions.Chunk(ChunkSize))
        {
            var filings = await dbContext
                .Set<BeneficialOwnershipFiling>()
                .AsNoTracking()
                .Where(filing => chunk.Contains(filing.AccessionNumber))
                .Select(filing => new { filing.AccessionNumber, filing.Rules })
                .ToListAsync(cancellationToken);
            foreach (var filing in filings)
                rules[filing.AccessionNumber] = filing.Rules;
        }

        return standing
            .GroupBy(stake => Key(stake, primaryTickers.GetValueOrDefault(stake.EquityIssuerId)))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var schedule13G = group
                        .Where(stake => stake.FilingType == FilingType.Schedule13G)
                        .Select(stake =>
                            stake.AccessionNumber != null
                                ? rules.GetValueOrDefault(stake.AccessionNumber)
                                : Schedule13GRules.None
                        )
                        .ToList();
                    return new Schedule13DGCounts
                    {
                        Schedule13D = group.Count(stake =>
                            stake.FilingType == FilingType.Schedule13D
                        ),
                        Schedule13G = schedule13G.Count,
                        QualifiedInstitutions = schedule13G.Count(rule =>
                            rule.HasFlag(Schedule13GRules.QualifiedInstitution)
                        ),
                        PassiveInvestors = schedule13G.Count(rule =>
                            rule.HasFlag(Schedule13GRules.PassiveInvestor)
                        ),
                        ExemptInvestors = schedule13G.Count(rule =>
                            rule.HasFlag(Schedule13GRules.ExemptInvestor)
                        ),
                    };
                }
            );
    }

    private static HolderSecurityKey Key(Stake stake, string primaryTicker) =>
        new(
            stake.EquityIssuerId,
            stake.ListedTicker == null
            || string.Equals(stake.ListedTicker, primaryTicker, StringComparison.OrdinalIgnoreCase)
                ? null
                : stake.ListedTicker
        );

    private sealed class Stake
    {
        public Guid Id { get; init; }
        public Guid InstitutionalHolderId { get; init; }
        public Guid EquityIssuerId { get; init; }
        public string ListedTicker { get; init; }
        public DateOnly EventDate { get; init; }
        public DateOnly FilingDate { get; init; }
        public string AccessionNumber { get; init; }
        public FilingType FilingType { get; init; }
        public long Shares { get; init; }
        public decimal? PercentOfClass { get; init; }
    }
}

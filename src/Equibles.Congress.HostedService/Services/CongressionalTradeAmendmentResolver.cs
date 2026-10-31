using Equibles.Congress.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Supersedes the rows amending House PTRs correct or withdraw. Runs every cycle over unresolved
/// amendment rows, so it converges whichever filing was read first; the sync's persist step then
/// keeps a superseded row from returning when its filing is read again.
/// </summary>
[Service]
public class CongressionalTradeAmendmentResolver
{
    internal const string AmbiguousErrorContext = "CongressTrades.AmendmentAmbiguous";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<CongressionalTradeAmendmentResolver> _logger;

    public CongressionalTradeAmendmentResolver(
        EquiblesFinancialDbContext dbContext,
        ErrorReporter errorReporter,
        ILogger<CongressionalTradeAmendmentResolver> logger
    )
    {
        _dbContext = dbContext;
        _errorReporter = errorReporter;
        _logger = logger;
    }

    public virtual async Task<int> ResolvePending(CancellationToken ct)
    {
        var pending = await _dbContext
            .Set<CongressionalTradeAmendment>()
            .Where(row =>
                row.Verdict == CongressionalTradeAmendmentVerdict.Pending
                || row.Verdict == CongressionalTradeAmendmentVerdict.NoPredecessor
            )
            .ToListAsync(ct);

        // Oldest amendment first, so an amendment of an amendment sees the first one resolved.
        var filings = pending
            .GroupBy(row => (row.FilingKind, row.SourceId))
            .OrderBy(group => group.Min(row => row.FilingDate))
            .ThenBy(group => group.Key.SourceId, StringComparer.Ordinal)
            .ToList();

        var superseded = 0;
        foreach (var filing in filings)
        {
            var rows = filing.OrderBy(row => row.SourceRowIndex).ToList();
            superseded += await Resolve(rows, ct);
        }

        if (superseded > 0)
        {
            _logger.LogInformation(
                "Superseded {Count} congressional trades corrected or withdrawn by amending reports",
                superseded
            );
        }

        return superseded;
    }

    private async Task<int> Resolve(List<CongressionalTradeAmendment> rows, CancellationToken ct)
    {
        var first = rows[0];
        var kind = (CongressionalFilingKind?)first.FilingKind;
        var memberIds = rows.Select(row => row.CongressMemberId).Distinct().ToList();
        var tickers = rows.Select(row => row.FiledTicker).Distinct().ToList();
        var filingDate = rows.Max(row => row.FilingDate);
        var candidates = await _dbContext
            .Set<CongressionalTrade>()
            .Where(trade =>
                trade.FilingKind == kind
                && trade.SourceId != null
                && trade.SourceRowIndex != null
                && trade.SourceId != first.SourceId
                && memberIds.Contains(trade.CongressMemberId)
                && tickers.Contains(trade.FiledTicker)
                && trade.FilingDate <= filingDate
            )
            .ToListAsync(ct);

        var match =
            memberIds.Count == 1
                ? CongressionalTradeAmendmentMatcher.Match(rows, candidates)
                : new AmendmentMatch(CongressionalTradeAmendmentVerdict.Ambiguous, []);
        var now = DateTime.UtcNow;
        switch (match.Verdict)
        {
            case CongressionalTradeAmendmentVerdict.Matched:
                await ApplyMatch(rows, match.Pairs, now, ct);
                return match.Pairs.Count;

            case CongressionalTradeAmendmentVerdict.Ambiguous:
                foreach (var row in rows)
                {
                    row.Verdict = CongressionalTradeAmendmentVerdict.Ambiguous;
                    row.ResolvedAt = now;
                }
                await _dbContext.SaveChangesAsync(ct);
                await _errorReporter.Report(
                    ErrorSource.CongressScraper,
                    AmbiguousErrorContext,
                    DescribeAmbiguous(first, rows.Count),
                    stackTrace: null
                );
                return 0;

            default:
                foreach (var row in rows)
                    row.Verdict = CongressionalTradeAmendmentVerdict.NoPredecessor;
                await _dbContext.SaveChangesAsync(ct);
                return 0;
        }
    }

    private async Task ApplyMatch(
        List<CongressionalTradeAmendment> rows,
        IReadOnlyList<AmendmentPair> pairs,
        DateTime now,
        CancellationToken ct
    )
    {
        var first = rows[0];
        var kind = (CongressionalFilingKind?)first.FilingKind;
        var amendedRows = await _dbContext
            .Set<CongressionalTrade>()
            .Where(trade => trade.FilingKind == kind && trade.SourceId == first.SourceId)
            .ToListAsync(ct);

        foreach (var pair in pairs)
        {
            var row = pair.Amendment;
            row.Verdict = CongressionalTradeAmendmentVerdict.Matched;
            row.SupersededSourceId = pair.Superseded.SourceId;
            row.SupersededSourceRowIndex = pair.Superseded.SourceRowIndex;
            // A superseded amended row already carries its own first disclosure, so chains keep
            // the earliest date.
            row.FirstDisclosedDate = pair.Superseded.FilingDate;
            row.ResolvedAt = now;
            _dbContext.Remove(pair.Superseded);

            if (row.Status != CongressionalTradeFilingStatus.Amended)
                continue;
            var amended = amendedRows.SingleOrDefault(trade =>
                trade.SourceRowIndex == row.SourceRowIndex
            );
            if (amended != null)
                amended.FilingDate = pair.Superseded.FilingDate;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    internal static string DescribeAmbiguous(CongressionalTradeAmendment first, int rowCount) =>
        $"{first.FilingKind} {first.SourceId} filed {first.FilingDate:yyyy-MM-dd}: {rowCount} amended "
        + "or deleted rows match more than one earlier filing of the member, or cannot pair "
        + "one-to-one with one; no stored trade was superseded";
}

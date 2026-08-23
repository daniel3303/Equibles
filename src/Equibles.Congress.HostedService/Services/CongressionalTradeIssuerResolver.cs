using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Resolves a filed congressional ticker to an issuer only when dated SEC cover-page evidence
/// brackets the trade without an ownership change. A ticker-reuse gap is intentionally null.
/// </summary>
[Service]
public class CongressionalTradeIssuerResolver
{
    private readonly CommonStockTickerEvidenceRepository _evidenceRepository;
    private readonly EquiblesFinancialDbContext _dbContext;

    public CongressionalTradeIssuerResolver(
        CommonStockTickerEvidenceRepository evidenceRepository,
        EquiblesFinancialDbContext dbContext
    )
    {
        _evidenceRepository = evidenceRepository;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Reconsiders stored unlinked source rows after new SEC evidence arrives. This is what makes
    /// an initially one-sided observation converge later without re-fetching the congressional
    /// filing; already-linked rows remain immutable outside an explicit parser-version replay.
    /// </summary>
    public virtual async Task<int> RelinkUnresolved(CancellationToken cancellationToken)
    {
        var evidence = _dbContext.Set<CommonStockTickerEvidence>();
        var unresolved = await _dbContext
            .Set<CongressionalTrade>()
            .Where(trade =>
                trade.CommonStockId == null
                && trade.FiledTicker != ""
                && (
                    evidence.Any(row =>
                        row.Ticker == trade.FiledTicker && row.FiledDate == trade.TransactionDate
                    )
                    || (
                        evidence.Any(row =>
                            row.Ticker == trade.FiledTicker && row.FiledDate < trade.TransactionDate
                        )
                        && evidence.Any(row =>
                            row.Ticker == trade.FiledTicker && row.FiledDate > trade.TransactionDate
                        )
                    )
                )
            )
            .ToListAsync(cancellationToken);
        if (unresolved.Count == 0)
            return 0;

        var lookupTransactions = unresolved
            .Select(trade => new DisclosureTransaction
            {
                MemberName = "",
                Ticker = trade.FiledTicker,
                TransactionDate = trade.TransactionDate,
            })
            .ToList();
        var resolutions = await Resolve(lookupTransactions, cancellationToken);
        var linked = 0;
        for (var index = 0; index < unresolved.Count; index++)
        {
            var issuerId = resolutions[lookupTransactions[index]];
            if (!issuerId.HasValue)
                continue;

            unresolved[index].CommonStockId = issuerId;
            linked++;
        }

        if (linked > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);
        return linked;
    }

    public virtual async Task<IReadOnlyDictionary<DisclosureTransaction, Guid?>> Resolve(
        IReadOnlyCollection<DisclosureTransaction> transactions,
        CancellationToken cancellationToken
    )
    {
        var normalizedByTransaction = transactions.ToDictionary(
            transaction => transaction,
            transaction => TickerNormalizer.NormalizeIdentity(transaction.Ticker)
        );
        var tickers = normalizedByTransaction.Values.Where(ticker => ticker != null).Distinct();
        var evidence = await _evidenceRepository
            .GetByTickers(tickers)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byTicker = evidence
            .GroupBy(row => row.Ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        return transactions.ToDictionary(
            transaction => transaction,
            transaction =>
            {
                var ticker = normalizedByTransaction[transaction];
                return ticker != null && byTicker.TryGetValue(ticker, out var observations)
                    ? ResolveAtDate(observations, transaction.TransactionDate)
                    : null;
            }
        );
    }

    internal static Guid? ResolveAtDate(
        IReadOnlyCollection<CommonStockTickerEvidence> evidence,
        DateOnly transactionDate
    )
    {
        var exactIssuers = IssuersAt(evidence, transactionDate);
        if (exactIssuers.Count > 0)
            return exactIssuers.Count == 1 ? exactIssuers[0] : null;

        var beforeDate = evidence
            .Where(row => row.FiledDate < transactionDate)
            .Select(row => (DateOnly?)row.FiledDate)
            .Max();
        var afterDate = evidence
            .Where(row => row.FiledDate > transactionDate)
            .Select(row => (DateOnly?)row.FiledDate)
            .Min();
        if (!beforeDate.HasValue || !afterDate.HasValue)
            return null;

        var before = UniqueIssuerAt(evidence, beforeDate.Value);
        var after = UniqueIssuerAt(evidence, afterDate.Value);
        return before.HasValue && before == after ? before : null;
    }

    private static Guid? UniqueIssuerAt(
        IEnumerable<CommonStockTickerEvidence> evidence,
        DateOnly filedDate
    )
    {
        var issuers = IssuersAt(evidence, filedDate);
        return issuers.Count == 1 ? issuers[0] : null;
    }

    private static List<Guid> IssuersAt(
        IEnumerable<CommonStockTickerEvidence> evidence,
        DateOnly filedDate
    ) =>
        evidence
            .Where(row => row.FiledDate == filedDate)
            .Select(row => row.CommonStockId)
            .Distinct()
            .Take(2)
            .ToList();
}

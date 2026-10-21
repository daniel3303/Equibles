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
/// Resolves a filed congressional ticker to an issuer only when dated SEC evidence brackets the
/// trade without an ownership change. Two sources are dated: cover-page symbols and the SEC ticker
/// directory, whose spans observe a ticker on every day they cover. A ticker-reuse gap is
/// intentionally null.
/// </summary>
[Service]
public class CongressionalTradeIssuerResolver
{
    private readonly EquityIssuerTickerEvidenceRepository _evidenceRepository;
    private readonly EquiblesFinancialDbContext _dbContext;

    public CongressionalTradeIssuerResolver(
        EquityIssuerTickerEvidenceRepository evidenceRepository,
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
        var evidence = _dbContext.Set<EquityIssuerTickerEvidence>();
        var directory = _dbContext.Set<SecTickerDirectoryObservation>();
        // The same exact-date or both-sides shape ResolveAtDate needs, over both sources, so only
        // trades that could link are loaded.
        var unresolved = await _dbContext
            .Set<CongressionalTrade>()
            .Where(trade =>
                trade.EquityIssuerId == null
                && trade.FiledTicker != ""
                && (
                    evidence.Any(row =>
                        row.Ticker == trade.FiledTicker && row.FiledDate == trade.TransactionDate
                    )
                    || directory.Any(row =>
                        row.Ticker == trade.FiledTicker
                        && row.FirstObservedOn <= trade.TransactionDate
                        && row.LastObservedOn >= trade.TransactionDate
                    )
                    || (
                        (
                            evidence.Any(row =>
                                row.Ticker == trade.FiledTicker
                                && row.FiledDate < trade.TransactionDate
                            )
                            || directory.Any(row =>
                                row.Ticker == trade.FiledTicker
                                && row.FirstObservedOn < trade.TransactionDate
                            )
                        )
                        && (
                            evidence.Any(row =>
                                row.Ticker == trade.FiledTicker
                                && row.FiledDate > trade.TransactionDate
                            )
                            || directory.Any(row =>
                                row.Ticker == trade.FiledTicker
                                && row.LastObservedOn > trade.TransactionDate
                            )
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

            unresolved[index].EquityIssuerId = issuerId;
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
        var tickers = normalizedByTransaction
            .Values.Where(ticker => ticker != null)
            .Distinct()
            .ToList();
        var evidence = await _evidenceRepository
            .GetByTickers(tickers)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var coverPagesByTicker = evidence
            .GroupBy(row => row.Ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var directoryByTicker = (await LoadDirectorySpans(tickers, cancellationToken))
            .GroupBy(span => span.Ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        return transactions.ToDictionary(
            transaction => transaction,
            transaction =>
            {
                var ticker = normalizedByTransaction[transaction];
                if (ticker == null)
                    return null;
                return ResolveAtDate(
                    coverPagesByTicker.GetValueOrDefault(ticker) ?? [],
                    directoryByTicker.GetValueOrDefault(ticker) ?? [],
                    transaction.TransactionDate
                );
            }
        );
    }

    // A directory CIK no issuer carries still names someone, so its span keeps a null issuer and
    // contests the date it covers instead of vanishing.
    private async Task<List<DirectorySpan>> LoadDirectorySpans(
        List<string> tickers,
        CancellationToken cancellationToken
    )
    {
        var rows = await (
            from span in _dbContext.Set<SecTickerDirectoryObservation>().AsNoTracking()
            where tickers.Contains(span.Ticker)
            join issuer in _dbContext.Set<EquityIssuer>() on span.Cik equals issuer.Cik into owners
            from owner in owners.DefaultIfEmpty()
            select new
            {
                span.Ticker,
                IssuerId = (Guid?)owner.Id,
                span.FirstObservedOn,
                span.LastObservedOn,
            }
        ).ToListAsync(cancellationToken);
        return rows.Select(row => new DirectorySpan(
                row.Ticker,
                row.IssuerId,
                row.FirstObservedOn,
                row.LastObservedOn
            ))
            .ToList();
    }

    internal static Guid? ResolveAtDate(
        IReadOnlyCollection<EquityIssuerTickerEvidence> evidence,
        DateOnly transactionDate
    ) => ResolveAtDate(evidence, [], transactionDate);

    /// <summary>
    /// A cover page observes its ticker on its filed date; a directory span observes it on its
    /// first and last day and on every day between, so a trade inside a span has evidence on the
    /// date itself. Otherwise the nearest observation on each side must name the same issuer.
    /// </summary>
    internal static Guid? ResolveAtDate(
        IReadOnlyCollection<EquityIssuerTickerEvidence> coverPages,
        IReadOnlyCollection<DirectorySpan> directory,
        DateOnly transactionDate
    )
    {
        var observations = coverPages
            .Select(row => new TickerObservation(row.EquityIssuerId, row.FiledDate))
            .Concat(
                directory.SelectMany(span =>
                    span.FirstObservedOn <= transactionDate
                    && span.LastObservedOn >= transactionDate
                        ? new[] { new TickerObservation(span.EquityIssuerId, transactionDate) }
                        : new[]
                        {
                            new TickerObservation(span.EquityIssuerId, span.FirstObservedOn),
                            new TickerObservation(span.EquityIssuerId, span.LastObservedOn),
                        }
                )
            )
            .ToList();

        var exactIssuers = IssuersAt(observations, transactionDate);
        if (exactIssuers.Count > 0)
            return exactIssuers.Count == 1 ? exactIssuers[0] : null;

        var beforeDate = observations
            .Where(row => row.ObservedOn < transactionDate)
            .Select(row => (DateOnly?)row.ObservedOn)
            .Max();
        var afterDate = observations
            .Where(row => row.ObservedOn > transactionDate)
            .Select(row => (DateOnly?)row.ObservedOn)
            .Min();
        if (!beforeDate.HasValue || !afterDate.HasValue)
            return null;

        var before = IssuersAt(observations, beforeDate.Value);
        var after = IssuersAt(observations, afterDate.Value);
        return before.Count == 1 && after.Count == 1 && before[0] == after[0] ? before[0] : null;
    }

    // A null issuer is one the store does not carry: it counts as a distinct owner and never links.
    private static List<Guid?> IssuersAt(
        IEnumerable<TickerObservation> observations,
        DateOnly observedOn
    ) =>
        observations
            .Where(row => row.ObservedOn == observedOn)
            .Select(row => row.EquityIssuerId)
            .Distinct()
            .Take(2)
            .ToList();

    internal sealed record DirectorySpan(
        string Ticker,
        Guid? EquityIssuerId,
        DateOnly FirstObservedOn,
        DateOnly LastObservedOn
    );

    private readonly record struct TickerObservation(Guid? EquityIssuerId, DateOnly ObservedOn);
}

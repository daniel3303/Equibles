using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories;

/// <summary>
/// Each insider's position in an issuer from their Forms 3, 4 and 5: the newest filing's closing
/// balance per bucket, restated onto today's split basis. GetInsiderOwnership and the unified holder
/// list read it, so the two never disagree on an insider's holding.
/// </summary>
public class InsiderOwnershipPositionCalculator
{
    private readonly InsiderTransactionRepository _transactionRepository;

    public InsiderOwnershipPositionCalculator(InsiderTransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    /// <summary>Every insider's position from their newest filing, unordered.</summary>
    public async Task<List<InsiderOwnershipPosition>> GetCurrentPositions(
        EquityIssuer stock,
        List<StockSplit> splits,
        CancellationToken cancellationToken = default
    )
    {
        // Materialize every row before ranking: each sits on its own split basis.
        var rows = await _transactionRepository
            .GetCurrentPositionRows(stock.Id)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(t => t.InsiderOwnerId)
            .Select(group => Build([.. group], splits))
            .ToList();
    }

    /// <summary>
    /// Each insider's position from the filing before the one <paramref name="current"/> reads,
    /// keyed by insider. An insider whose newest filing has no accession number, or who has no
    /// earlier filing, has none.
    /// </summary>
    public async Task<Dictionary<Guid, InsiderOwnershipPosition>> GetPreviousPositions(
        EquityIssuer stock,
        IReadOnlyCollection<InsiderOwnershipPosition> current,
        List<StockSplit> splits,
        CancellationToken cancellationToken = default
    )
    {
        var currentAccessions = current
            .Where(position => position.Anchor.AccessionNumber != null)
            .ToDictionary(
                position => position.Anchor.InsiderOwnerId,
                position => position.Anchor.AccessionNumber
            );
        if (currentAccessions.Count == 0)
            return [];
        var filings = await _transactionRepository
            .GetFilingKeys(stock.Id)
            .ToListAsync(cancellationToken);
        var previous = filings
            .Where(filing => currentAccessions.ContainsKey(filing.InsiderOwnerId))
            .GroupBy(filing => filing.InsiderOwnerId)
            .Select(group =>
                group
                    .OrderByDescending(filing => filing.TransactionDate)
                    .ThenByDescending(filing => filing.FilingDate)
                    .ThenByDescending(filing => filing.AccessionNumber, StringComparer.Ordinal)
                    .SkipWhile(filing => filing.AccessionNumber != currentAccessions[group.Key])
                    .Skip(1)
                    .FirstOrDefault()
            )
            .OfType<InsiderFilingKey>()
            .ToList();
        if (previous.Count == 0)
            return [];
        var wanted = previous
            .Select(filing => (filing.InsiderOwnerId, filing.AccessionNumber))
            .ToHashSet();
        var rows = await _transactionRepository
            .GetFilingRows(
                stock.Id,
                previous.Select(filing => filing.AccessionNumber).Distinct().ToList()
            )
            .ToListAsync(cancellationToken);
        return rows.Where(row => wanted.Contains((row.InsiderOwnerId, row.AccessionNumber)))
            .GroupBy(row => row.InsiderOwnerId)
            .ToDictionary(group => group.Key, group => Build([.. group], splits));
    }

    /// <summary>
    /// One insider's position from one filing's rows. A multi-row filing lists intermediate
    /// balances per bucket, so only each (security title, direct/indirect) bucket's last row in
    /// filing order is its closing balance; those are restated onto today's split basis and summed
    /// per nature. The anchor is the filing's overall last row.
    /// </summary>
    public static InsiderOwnershipPosition Build(
        List<InsiderTransaction> rows,
        List<StockSplit> splits
    )
    {
        var ordered = rows.OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.FilingDate)
            .ThenByDescending(t => t.AccessionNumber, StringComparer.Ordinal)
            .ThenByDescending(t => t.TransactionOrder)
            .ThenByDescending(t => t.Id)
            .ToList();

        long direct = 0;
        long indirect = 0;
        var ambiguous = false;
        foreach (
            var bucket in ordered.GroupBy(t => (Title: t.SecurityTitle ?? "", t.OwnershipNature))
        )
        {
            if (
                bucket.Key.OwnershipNature != OwnershipNature.Direct
                && bucket.Select(t => t.SharesOwnedAfter).Distinct().Skip(1).Any()
            )
                ambiguous = true;
            var closing = bucket.First();
            var adjusted = SplitAdjustment.AdjustShareCount(
                closing.SharesOwnedAfter,
                closing.TransactionDate,
                splits
            );
            if (bucket.Key.OwnershipNature == OwnershipNature.Direct)
                direct += adjusted;
            else
                indirect += adjusted;
        }

        return new InsiderOwnershipPosition(ordered[0], direct, indirect)
        {
            IndirectBalancesAmbiguous = ambiguous,
        };
    }
}

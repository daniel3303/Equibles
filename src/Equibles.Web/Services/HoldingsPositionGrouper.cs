using Equibles.Holdings.Data.Models;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.Web.Services;

public static class HoldingsPositionGrouper
{
    public static Dictionary<PositionChangeType, List<HolderPositionChange>> Group(
        IReadOnlyList<InstitutionalHolding> currentHoldings,
        IReadOnlyList<InstitutionalHolding> previousHoldings
    )
    {
        var currentByHolder = AggregateByHolder(currentHoldings);
        var previousByHolder = AggregateByHolder(previousHoldings);

        var result = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.New] = [],
            [PositionChangeType.Increased] = [],
            [PositionChangeType.Reduced] = [],
            [PositionChangeType.Unchanged] = [],
            [PositionChangeType.SoldOut] = [],
        };

        foreach (var (holderId, current) in currentByHolder)
        {
            previousByHolder.TryGetValue(holderId, out var previous);

            var change = new HolderPositionChange
            {
                InstitutionalHolderId = holderId,
                InstitutionalHolder = current.Holder,
                CurrentHolding = current.LatestHolding,
                CurrentShares = current.TotalShares,
                CurrentValue = current.TotalValue,
                PreviousShares = previous?.TotalShares ?? 0,
                PreviousValue = previous?.TotalValue ?? 0,
                ChangeType = ClassifyChange(current.TotalShares, previous?.TotalShares ?? 0),
            };

            result[change.ChangeType].Add(change);
        }

        foreach (var (holderId, previous) in previousByHolder)
        {
            if (currentByHolder.ContainsKey(holderId))
                continue;

            result[PositionChangeType.SoldOut]
                .Add(
                    new HolderPositionChange
                    {
                        InstitutionalHolderId = holderId,
                        InstitutionalHolder = previous.Holder,
                        CurrentHolding = null,
                        CurrentShares = 0,
                        CurrentValue = 0,
                        PreviousShares = previous.TotalShares,
                        PreviousValue = previous.TotalValue,
                        ChangeType = PositionChangeType.SoldOut,
                    }
                );
        }

        return result;
    }

    private static PositionChangeType ClassifyChange(long currentShares, long previousShares)
    {
        if (previousShares == 0)
            return PositionChangeType.New;
        if (currentShares == previousShares)
            return PositionChangeType.Unchanged;
        return currentShares > previousShares
            ? PositionChangeType.Increased
            : PositionChangeType.Reduced;
    }

    private static Dictionary<Guid, HolderAggregate> AggregateByHolder(
        IReadOnlyList<InstitutionalHolding> holdings
    )
    {
        return holdings
            .GroupBy(h => h.InstitutionalHolderId)
            .ToDictionary(
                g => g.Key,
                g => new HolderAggregate
                {
                    Holder = g.First().InstitutionalHolder,
                    LatestHolding = g.OrderByDescending(h => h.FilingDate).First(),
                    TotalShares = g.Sum(h => h.Shares),
                    TotalValue = g.Sum(h => h.Value),
                }
            );
    }

    private class HolderAggregate
    {
        public InstitutionalHolder Holder { get; set; }
        public InstitutionalHolding LatestHolding { get; set; }
        public long TotalShares { get; set; }
        public long TotalValue { get; set; }
    }
}

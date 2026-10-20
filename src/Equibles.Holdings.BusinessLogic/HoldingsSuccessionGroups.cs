using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Groups <see cref="HolderSuccession"/> pairs into the filers that continue together across
/// one quarter, and decides which groups a quarter-over-quarter comparison can treat as one
/// holder: those whose successors' first 13F book is within a factor of
/// <see cref="MaximumBookRatio"/> of their predecessors' last.
/// </summary>
public static class HoldingsSuccessionGroups
{
    public const double MaximumBookRatio = HolderSuccession.MaximumComparableBookRatio;

    public static bool IsComparable(long predecessorValue, long successorValue) =>
        Math.Min(predecessorValue, successorValue) > 0
        && Math.Max(predecessorValue, successorValue)
            <= MaximumBookRatio * Math.Min(predecessorValue, successorValue);

    /// <summary>
    /// Splits pairs into groups that share a predecessor or a successor within one successor
    /// quarter. A holder can succeed one filer and later be succeeded itself, so quarters never join.
    /// </summary>
    public static List<List<T>> Connect<T>(
        IEnumerable<T> pairs,
        Func<T, DateOnly> successorQuarter,
        Func<T, Guid> predecessor,
        Func<T, Guid> successor
    )
    {
        var groups = new List<List<T>>();
        foreach (var quarter in pairs.GroupBy(successorQuarter))
        {
            var root = new Dictionary<Guid, Guid>();
            Guid Find(Guid id)
            {
                while (root.TryGetValue(id, out var parent) && parent != id)
                    id = parent;
                return id;
            }
            foreach (var pair in quarter)
            {
                var from = Find(predecessor(pair));
                var to = Find(successor(pair));
                if (from != to)
                    root[from] = to;
            }
            groups.AddRange(
                quarter.GroupBy(pair => Find(successor(pair))).Select(group => group.ToList())
            );
        }
        return groups;
    }

    /// <summary>
    /// Every group of stored pairs with the members' names. The refresh stamps each pair with
    /// its whole group's book values, so every pair of a group carries the same two values.
    /// </summary>
    public static List<HoldingsSuccessionGroup> Build(IEnumerable<HoldingsSuccessionPair> pairs) =>
        Connect(
                pairs,
                pair => pair.SuccessorFirstReportDate,
                pair => pair.PredecessorHolderId,
                pair => pair.SuccessorHolderId
            )
            .Select(group => new HoldingsSuccessionGroup(
                group[0].SuccessorFirstReportDate,
                group
                    .DistinctBy(pair => pair.PredecessorHolderId)
                    .ToDictionary(pair => pair.PredecessorHolderId, pair => pair.PredecessorName),
                group
                    .DistinctBy(pair => pair.SuccessorHolderId)
                    .ToDictionary(pair => pair.SuccessorHolderId, pair => pair.SuccessorName),
                group.All(pair =>
                    IsComparable(pair.PredecessorGroupValue, pair.SuccessorGroupValue)
                )
            ))
            .ToList();
}

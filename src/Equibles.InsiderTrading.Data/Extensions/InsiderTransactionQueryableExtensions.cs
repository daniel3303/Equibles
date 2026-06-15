using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Data.Extensions;

public static class InsiderTransactionQueryableExtensions
{
    /// <summary>
    /// Drops position-snapshot rows (<see cref="TransactionCode.Holding"/>) from a
    /// transaction query. Holdings are parsed from Form 3/4/5 holding elements and
    /// carry the insider's whole position in <see cref="InsiderTransaction.Shares"/>
    /// with no price, so listing them next to trades reads as a phantom acquisition
    /// of the entire stake. Apply on transaction lists and dollar-volume boards;
    /// never on ownership summaries (which read the position) or the reprocess
    /// (which re-tags the rows). Aggregates already filtered to Purchase/Sale don't
    /// need it — a Holding row is neither.
    /// </summary>
    public static IQueryable<InsiderTransaction> ExcludeHoldings(
        this IQueryable<InsiderTransaction> query
    )
    {
        return query.Where(t => t.TransactionCode != TransactionCode.Holding);
    }
}

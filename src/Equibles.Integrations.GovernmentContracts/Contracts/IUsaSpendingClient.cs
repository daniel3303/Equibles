using Equibles.Integrations.GovernmentContracts.Models;

namespace Equibles.Integrations.GovernmentContracts.Contracts;

public interface IUsaSpendingClient
{
    /// <summary>
    /// Fetches every federal procurement contract award (award types A/B/C/D) whose
    /// action date falls within <paramref name="startDate"/>..<paramref name="endDate"/>
    /// and whose award amount is at least <paramref name="minimumAmount"/>. Dense
    /// windows are handled internally with an amount-descending cursor (and, for
    /// pathological same-amount tie runs, date bisection), so callers get the complete
    /// set regardless of window density; only a single day with 10,000+ awards tied at
    /// the exact same amount is truncated, and that is logged loudly.
    /// </summary>
    Task<List<UsaSpendingAwardRecord>> GetContractAwards(
        DateOnly startDate,
        DateOnly endDate,
        decimal minimumAmount,
        CancellationToken cancellationToken = default
    );
}

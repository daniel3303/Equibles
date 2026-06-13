using Equibles.Integrations.GovernmentContracts.Models;

namespace Equibles.Integrations.GovernmentContracts.Contracts;

public interface IUsaSpendingClient
{
    /// <summary>
    /// Fetches federal procurement contract awards (award types A/B/C/D) whose
    /// action date falls within <paramref name="startDate"/>..<paramref name="endDate"/>
    /// and whose award amount is at least <paramref name="minimumAmount"/>, following
    /// pagination. The window must be narrow enough to stay under the API's
    /// deep-pagination ceiling; truncation is logged rather than silently dropped.
    /// </summary>
    Task<List<UsaSpendingAwardRecord>> GetContractAwards(
        DateOnly startDate,
        DateOnly endDate,
        decimal minimumAmount
    );
}

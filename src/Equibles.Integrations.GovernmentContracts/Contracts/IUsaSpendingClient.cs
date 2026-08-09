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

    /// <summary>
    /// Fetches the recipient profile for a level-qualified recipient hash (the
    /// <c>recipient_id</c> carried on award rows), exposing the SAM-registered corporate
    /// family. Returns null when the endpoint answers 404 — an unknown or profileless
    /// recipient is an answer, not a fault. Transport failures and server errors throw
    /// after the client's retry ladder, exactly like the award search.
    /// </summary>
    Task<UsaSpendingRecipientProfile> GetRecipientProfile(
        string recipientId,
        CancellationToken cancellationToken = default
    );
}

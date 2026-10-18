using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;

namespace Equibles.Congress.HostedService.Services;

public interface ICongressMemberIdentityService
{
    Task ReconcileMembers(CancellationToken ct);

    /// <summary>
    /// Reconciles the reviewed catalog, then assigns a BioGuide id to each unresolved member whose
    /// seat, filing dates and surname point at exactly one legislator.
    /// </summary>
    Task<LegislatorIdentityReconciliation> ReconcileLegislatorIdentities(CancellationToken ct);

    Task<Dictionary<string, CongressMember>> UpsertMembers(
        IReadOnlyCollection<CongressMemberObservation> observations,
        CancellationToken ct
    );
}

using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;

namespace Equibles.Congress.HostedService.Services;

public interface ICongressMemberIdentityService
{
    Task ReconcileMembers(CancellationToken ct);

    Task<Dictionary<string, CongressMember>> UpsertMembers(
        IReadOnlyCollection<CongressMemberObservation> observations,
        CancellationToken ct
    );
}

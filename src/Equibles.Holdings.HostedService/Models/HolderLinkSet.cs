using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>Everything one link refresh stores, replacing the previous set.</summary>
public record HolderLinkSet(
    List<HolderLinkEvidence> Evidence,
    List<HolderRollup> Rollups,
    List<FundSeriesHolderLink> FundSeriesLinks
);

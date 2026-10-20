using Equibles.Holdings.BusinessLogic.Ownership.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// Reads the purpose a Schedule 13D states in Item 4, for a deployment that classifies it. The
/// holder list and the 13D and 13G record show it beside each 13D; without a source, or for an
/// accession the source has no reading of, they show none.
/// </summary>
public interface IBeneficialOwnershipPurposeSource
{
    /// <summary>The reading of each accession it has one for; others are left out.</summary>
    Task<IReadOnlyDictionary<string, StatedPurpose>> Read(
        IReadOnlyCollection<string> accessionNumbers,
        CancellationToken cancellationToken = default
    );
}

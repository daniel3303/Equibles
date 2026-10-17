using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Models;

/// <summary>A holder's derived entity type and the evidence that decided it.</summary>
public record HolderEntityTypeDecision(
    HolderEntityType EntityType,
    HolderEvidenceSource? Source,
    string SourceKey,
    DateOnly? AsOf,
    bool HasConflictingEvidence
)
{
    public static readonly HolderEntityTypeDecision Unknown = new(
        HolderEntityType.Unknown,
        null,
        null,
        null,
        false
    );
}

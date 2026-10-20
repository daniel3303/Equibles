namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// Describes the purpose a Schedule 13D states in Item 4, for a deployment that classifies it. The
/// holder list and the 13D and 13G record show the description beside each 13D; without a source,
/// or for an accession it has no reading of, they show none.
/// </summary>
public interface IBeneficialOwnershipPurposeSource
{
    /// <summary>A short description per accession it has a reading of, such as "Control or board".</summary>
    Task<IReadOnlyDictionary<string, string>> Describe(
        IReadOnlyCollection<string> accessionNumbers,
        CancellationToken cancellationToken = default
    );
}

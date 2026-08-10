namespace Equibles.Holdings.Repositories.Models;

public sealed class InstitutionalHolderResolution
{
    public InstitutionalHolderSearchMatch Selected { get; init; }
    public IReadOnlyList<InstitutionalHolderSearchMatch> Candidates { get; init; } = [];

    public bool IsAmbiguous => Selected == null && Candidates.Count > 1;
}

namespace Equibles.GovernmentContracts.HostedService.Services;

/// <summary>
/// The single registrant a recipient profile supports as parent: a representative
/// level-qualified hash plus EVERY name that registrant has carried, so exact-match
/// resolution can try the current legal name and its predecessors alike (a renamed
/// registrant is one owner, and any of its names may be the one our stock universe
/// stores).
/// </summary>
public class RecipientParentChoice
{
    public string ParentId { get; set; }

    public List<string> Names { get; set; } = [];
}

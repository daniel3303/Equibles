using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// One batch of captured splits grouped by issuer once. Readers that scope splits for every row
/// of a market-wide set use it so the work stays linear in rows plus splits, never their product.
/// </summary>
public sealed class IssuerSplitIndex
{
    private readonly ILookup<Guid, StockSplit> _byIssuer;

    public IssuerSplitIndex(IEnumerable<StockSplit> splits)
    {
        _byIssuer = (splits ?? []).ToLookup(split => split.EquityIssuerId);
    }

    /// <summary>
    /// The issuer's splits in batch order, or an empty sequence when it has none.
    /// </summary>
    public IEnumerable<StockSplit> For(Guid issuerId) => _byIssuer[issuerId];
}

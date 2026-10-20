namespace Equibles.Holdings.HostedService.Services.Signals;

/// <summary>
/// One insider's newest share-bearing filing on an issuer, as InsiderSharesAsOfLoader reads it.
/// Public and unsealed because the lazy-loading proxy convention validates every type a
/// SqlQuery materialises.
/// </summary>
public class NewestInsiderRow
{
    public Guid EquityIssuerId { get; set; }
    public Guid InsiderOwnerId { get; set; }
    public string AccessionNumber { get; set; }
    public Guid Id { get; set; }
}

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// What a probe of a stored website concluded. <see cref="Unknown"/> covers a failed render or a
/// missing sidecar, neither of which is evidence against the site.
/// </summary>
public enum WebsiteLiveness
{
    Alive,
    Dead,
    Unknown,
}

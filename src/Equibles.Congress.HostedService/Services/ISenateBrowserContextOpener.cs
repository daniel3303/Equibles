using Microsoft.Playwright;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Opens the browser context a Senate eFD session runs in. The default opens one
/// direct context; a host that sends outbound traffic through proxies registers its
/// own to choose the connection, and may load the home page on several until the
/// site lets one in.
/// </summary>
public interface ISenateBrowserContextOpener
{
    /// <summary>
    /// Opens a context, runs <paramref name="loadHome"/> in it and returns it with
    /// the home page's answer, whatever that answer was. A context the opener does
    /// not return is closed before this returns.
    /// </summary>
    Task<SenateBrowserContext> Open(
        IBrowser browser,
        string homeUrl,
        Func<IBrowserContext, Task<IResponse>> loadHome
    );
}

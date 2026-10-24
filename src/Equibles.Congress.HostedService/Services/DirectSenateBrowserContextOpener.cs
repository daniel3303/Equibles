using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Opens the Senate eFD session's context on the host's own connection.
/// </summary>
[Service(ServiceLifetime.Singleton, typeof(ISenateBrowserContextOpener))]
public class DirectSenateBrowserContextOpener : ISenateBrowserContextOpener
{
    public async Task<SenateBrowserContext> Open(
        IBrowser browser,
        string homeUrl,
        Func<IBrowserContext, Task<IResponse>> loadHome
    )
    {
        var context = await browser.NewContextAsync();
        try
        {
            var home = await loadHome(context);
            return new SenateBrowserContext(home, () => new ValueTask(Close(context)));
        }
        catch
        {
            await Close(context);
            throw;
        }
    }

    private static async Task Close(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // A context whose browser already went away has nothing left to close.
        }
    }
}

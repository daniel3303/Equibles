using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

internal sealed class YahooChartIdentityClient(HttpClient client)
    : YahooFinanceClient(client, NullLogger<YahooFinanceClient>.Instance)
{
    protected override Task<(string Crumb, string CookieHeader)> EnsureSession() =>
        Task.FromResult(("test-crumb", "A=1"));
}

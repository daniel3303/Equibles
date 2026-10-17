using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;

namespace Equibles.UnitTests.Helpers;

/// <summary>
/// A Yahoo client whose cookie-and-crumb handshake is canned. The real handshake uses its own
/// HttpClient against Yahoo, so a test built on the plain client fails whenever Yahoo throttles CI.
/// </summary>
internal sealed class OfflineYahooFinanceClient : YahooFinanceClient
{
    public OfflineYahooFinanceClient(HttpClient httpClient, ILogger<YahooFinanceClient> logger)
        : base(httpClient, logger) { }

    protected override Task<(string Crumb, string CookieHeader)> EnsureSession() =>
        Task.FromResult(("test-crumb", "A=1"));
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Contract: GetDailyShortVolume requests the official FINRA file names with a
/// Gregorian yyyyMMdd stamp, regardless of the caller's current culture.
/// </summary>
public class FinraClientGetDailyShortVolumeCultureTests
{
    [Fact]
    public void GetDailyShortVolume_HijriCultureThread_UsesGregorianDateInFileNames()
    {
        var requestedPaths = new ConcurrentBag<string>();
        var handler = new CaptureRequestHandler(requestedPaths.Add);
        var options = Options.Create(new FinraOptions { ClientId = "test", ClientSecret = "test" });
        var sut = new FinraClient(
            new HttpClient(handler),
            NullLogger<FinraClient>.Instance,
            options
        );

        // Run the culture-sensitive call on a dedicated thread that owns and
        // restores its own culture. The original form set CurrentCulture on the
        // xUnit thread-pool thread, then awaited: the await continuation could
        // resume on a different thread, so the finally restored the culture on the
        // continuation thread while the original pooled thread stayed ar-SA. A
        // sibling Finra test reusing that pooled thread then inherited Hijri
        // formatting and failed intermittently. A dedicated thread removes the race.
        var worker = new Thread(() =>
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            sut.GetDailyShortVolume(new DateOnly(2024, 3, 15)).GetAwaiter().GetResult();
        });
        worker.Start();
        worker.Join();

        requestedPaths
            .Should()
            .BeEquivalentTo(
                "/equity/regsho/daily/CNMSshvol20240315.txt",
                "/equity/regsho/daily/FORFshvol20240315.txt"
            );
    }

    private sealed class CaptureRequestHandler : HttpMessageHandler
    {
        private const string File =
            "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market\n"
            + "20240315|AAPL|1.25|0|2.5|Q\n"
            + "1\n";
        private readonly Action<string> _capture;

        public CaptureRequestHandler(Action<string> capture) => _capture = capture;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            _capture(request.RequestUri!.AbsolutePath);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File) }
            );
        }
    }
}

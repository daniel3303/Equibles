using System.Globalization;
using System.Net;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Contract: GetDailyShortVolume sends date-range filters to the FINRA API
/// using Gregorian ISO dates. ToString("yyyy-MM-dd") without InvariantCulture
/// produces Hijri dates on ar-SA threads, causing the API to return no results.
/// </summary>
public class FinraClientGetDailyShortVolumeCultureTests
{
    [Fact]
    public void GetDailyShortVolume_HijriCultureThread_SendsGregorianDateInRequest()
    {
        string capturedBody = null;
        var handler = new CaptureBodyHandler(body =>
        {
            capturedBody = body;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });
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

        capturedBody.Should().Contain("2024-03-15", "date filter must be Gregorian, not Hijri");
    }

    private class CaptureBodyHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _onData;

        public CaptureBodyHandler(Func<string, HttpResponseMessage> onData) => _onData = onData;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            // Route purely by URL, not by call order. FinraClient caches the OAuth
            // token in static fields shared across the whole test process, so a
            // parallel test can warm the cache and make this client skip the token
            // request entirely — the data POST then arrives first. An order-based
            // ("first request is the token") handler would answer that data POST
            // with the token object, which fails to deserialize into a record list
            // and crashes the test host. URL-based routing is order-independent.
            if (request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"test\",\"expires_in\":3600}"),
                };
            }

            var body =
                request.Content != null
                    ? await request.Content.ReadAsStringAsync(cancellationToken)
                    : string.Empty;
            return _onData(body);
        }
    }
}

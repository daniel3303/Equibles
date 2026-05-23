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
    [Fact(
        Skip = "GH-1915 — GetDailyShortVolume sends Hijri dates on non-Gregorian culture threads"
    )]
    public async Task GetDailyShortVolume_HijriCultureThread_SendsGregorianDateInRequest()
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

        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            await sut.GetDailyShortVolume(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }

        capturedBody.Should().Contain("2024-03-15", "date filter must be Gregorian, not Hijri");
    }

    private class CaptureBodyHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _onData;
        private bool _tokenReturned;

        public CaptureBodyHandler(Func<string, HttpResponseMessage> onData) => _onData = onData;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (!_tokenReturned || request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token"))
            {
                _tokenReturned = true;
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

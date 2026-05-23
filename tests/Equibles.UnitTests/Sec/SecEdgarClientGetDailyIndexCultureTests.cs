using System.Globalization;
using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// GetDailyIndex builds the SEC EDGAR master-index URL using string
/// interpolation {date:yyyyMMdd}. Without InvariantCulture the date portion
/// uses the thread's Hijri calendar on ar-SA, producing a URL that 404s.
/// </summary>
public class SecEdgarClientGetDailyIndexCultureTests
{
    [Fact]
    public async Task GetDailyIndex_HijriCultureThread_UrlContainsGregorianDate()
    {
        string capturedUrl = null;
        var handler = new CaptureUrlHandler(url =>
        {
            capturedUrl = url;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Sec:ContactEmail", "test@test.com")])
            .Build();
        var client = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            await client.GetDailyIndex(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }

        capturedUrl.Should().Contain("20240315", "URL date must be Gregorian, not Hijri");
    }

    private class CaptureUrlHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _factory;

        public CaptureUrlHandler(Func<string, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_factory(request.RequestUri!.ToString()));
    }
}

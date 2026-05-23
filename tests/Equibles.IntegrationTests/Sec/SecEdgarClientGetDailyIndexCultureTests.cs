using System.Globalization;
using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// GetDailyIndex builds the master-index URL using string interpolation
/// with {date:yyyyMMdd}. Without InvariantCulture the format uses the
/// thread's calendar — on ar-SA (Hijri) the URL contains a Hijri date
/// instead of Gregorian, causing a 404 against SEC EDGAR.
/// </summary>
public class SecEdgarClientGetDailyIndexCultureTests
{
    [Fact(
        Skip = "GH-1901 — GetDailyIndex builds URL with Hijri date on non-Gregorian culture threads"
    )]
    public async Task GetDailyIndex_HijriCultureThread_RequestsGregorianDateInUrl()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            string capturedUrl = null;
            var handler = new CapturingHandler(url =>
            {
                capturedUrl = url;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
                )
                .Build();
            var sut = new SecEdgarClient(
                new HttpClient(handler),
                Substitute.For<ILogger<SecEdgarClient>>(),
                config
            );

            await sut.GetDailyIndex(new DateOnly(2024, 3, 15));

            capturedUrl.Should().Contain("20240315", "URL date must be Gregorian, not Hijri");
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;

        public CapturingHandler(Func<string, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_respond(request.RequestUri!.ToString()));
    }
}

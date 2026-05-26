using System.Net;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Companion to FredClientTests (pagination pin) and the rate-limit retry pins.
/// `GetObservations` accepts an optional `DateOnly? startDate`; when supplied,
/// the body appends `&observation_start={value:yyyy-MM-dd}` to the FRED URL.
/// A refactor that drops the if-branch or changes the format string would
/// silently re-fetch the entire series on every incremental sync, blowing past
/// FRED's daily request budget. Pin the exact-format url contract.
/// </summary>
public class FredClientGetObservationsStartDateTests
{
    [Fact]
    public async Task GetObservations_StartDateSupplied_AppendsObservationStartParameter()
    {
        var emptyPayload = """{"count":0,"offset":0,"observations":[]}""";
        var handler = new CapturingHandler(emptyPayload);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FredOptions { ApiKey = "test-key" });
        var sut = new FredClient(httpClient, Substitute.For<ILogger<FredClient>>(), options);

        await sut.GetObservations("DGS10", new DateOnly(2024, 3, 15));

        handler.Requests.Should().NotBeEmpty();
        var query = handler.Requests[0].RequestUri!.Query;
        query.Should().Contain("observation_start=2024-03-15");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _payload;
        public List<HttpRequestMessage> Requests { get; } = new();

        public CapturingHandler(string payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_payload) }
            );
        }
    }
}

using System.Net;
using Equibles.Integrations.GovernmentContracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins the <c>date_type</c> on every window filter <see cref="UsaSpendingClient"/> sends.
///
/// USAspending's <c>time_period</c> filter defaults to period-of-performance OVERLAP when
/// <c>date_type</c> is omitted, so the window silently selects every contract merely IN FORCE
/// on those dates instead of those actioned in them. That is wrong twice over: the scan's
/// cursor and checkpoint are both keyed on the award action date, and the result set is far
/// too large to enumerate — measured against the live API, one day at the $1M floor returned
/// 118,159 awards (~1,180 pages) omitting it versus 169 with <c>action_date</c>. In production
/// that made a 1-day window unfinishable: the import burned ~256 requests per cycle, never
/// completed a window, never advanced the checkpoint, and refilled the error log every cycle.
/// It is a silent failure — the request still returns 200 with plausible awards — so it needs
/// a test rather than a code comment.
/// </summary>
public class UsaSpendingClientWindowDateTypeTests
{
    [Fact]
    public async Task GetContractAwards_SendsActionDateWindows()
    {
        var handler = new BodyCapturingHandler();
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        await sut.GetContractAwards(
            new DateOnly(2022, 1, 20),
            new DateOnly(2022, 1, 20),
            minimumAmount: 1_000_000m
        );

        handler.Bodies.Should().NotBeEmpty("the client must have queried the window");
        foreach (var body in handler.Bodies)
        {
            var period = (JObject)body["filters"]!["time_period"]![0]!;
            // Bind first: `period["date_type"]?.Value<string>().Should()...` would short-circuit
            // the whole assertion chain on the null this test exists to catch, and pass silently.
            var dateType = period["date_type"]?.Value<string>();
            dateType
                .Should()
                .Be(
                    "action_date",
                    "omitting date_type makes USAspending match contracts in force over the "
                        + "window rather than those actioned in it — a 700x larger, wrongly-keyed "
                        + "result set that no window can page through"
                );
            period["start_date"]!.Value<string>().Should().Be("2022-01-20");
            period["end_date"]!.Value<string>().Should().Be("2022-01-20");
        }
    }

    /// <summary>
    /// Records every request body and answers with a single terminal page, so the client
    /// issues exactly the one query the assertions inspect.
    /// </summary>
    private sealed class BodyCapturingHandler : HttpMessageHandler
    {
        public List<JObject> Bodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Bodies.Add(JObject.Parse(request.Content!.ReadAsStringAsync(cancellationToken).Result));

            var payload = new
            {
                results = Array.Empty<Dictionary<string, object>>(),
                page_metadata = new { page = 1, hasNext = false },
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload)),
                }
            );
        }
    }
}

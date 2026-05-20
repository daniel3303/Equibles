using System.Net.Http.Headers;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Services.Activity;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class StatusControllerActivityStreamTests
{
    private readonly WebHostFixture _host;

    public StatusControllerActivityStreamTests(WebHostFixture host) => _host = host;

    [Fact]
    public async Task ActivityStream_PublishedAfterConnect_StreamsTheEventToTheClient()
    {
        // Wait until the response headers come back but keep the body open —
        // SSE never closes, so the default ResponseContentRead would hang.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Status/Activity/Stream");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _host.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token
        );

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        response.Headers.TryGetValues("X-Activity-Stream", out var marker).Should().BeTrue();
        marker.Should().Contain("scraper");

        // Now publish — the SSE endpoint subscribes during the request, so any
        // event raised after that point flows out the body as SSE frames.
        using var bodyScope = _host.Services.CreateScope();
        var broadcaster = bodyScope.ServiceProvider.GetRequiredService<ActivityFeedBroadcaster>();
        var activity = new ScraperActivity(
            Source: "SEC",
            Severity: ScraperActivitySeverity.Info,
            Message: "fetching 10-K",
            Timestamp: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            CorrelationId: "corr-1"
        );
        broadcaster.Publish(activity);

        var frame = await ReadOneEventFrame(response, cts.Token);

        frame.Should().Contain("event: activity");
        frame.Should().Contain("\"Source\":\"SEC\"");
        frame.Should().Contain("\"Severity\":\"Info\"");
        frame.Should().Contain("\"Message\":\"fetching 10-K\"");
        frame.Should().Contain("\"CorrelationId\":\"corr-1\"");
    }

    private static async Task<string> ReadOneEventFrame(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new System.Text.StringBuilder();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }
            if (line.Length == 0)
            {
                if (buffer.Length > 0)
                {
                    return buffer.ToString();
                }
                continue;
            }
            buffer.AppendLine(line);
        }
        return buffer.ToString();
    }
}

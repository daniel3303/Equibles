using System.Net.Http.Headers;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Services.Activity;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to <see cref="StatusControllerActivityStreamTests"/>. That test
/// pins the LIVE path (publish after connect). This pins the BACKLOG path
/// (publish before connect) — the controller documents it explicitly:
/// "Replay the small ring buffer so a freshly-opened tab has context, then
/// forward live events". A regression that dropped the
/// <c>foreach (subscription.Backlog)</c> loop would still pass the live pin.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StatusControllerActivityStreamBacklogTests
{
    private readonly WebHostFixture _host;

    public StatusControllerActivityStreamBacklogTests(WebHostFixture host) => _host = host;

    [Fact]
    public async Task ActivityStream_PublishedBeforeConnect_ReplaysBacklogToTheClient()
    {
        // Publish first so the broadcaster's ring buffer holds the event when
        // the SSE handler later calls Subscribe(). A unique CorrelationId keeps
        // the assertion immune to events left in the shared singleton buffer
        // by other tests in this collection.
        var correlationId = "backlog-" + Guid.NewGuid().ToString("N");
        using (var seedScope = _host.Services.CreateScope())
        {
            var broadcaster =
                seedScope.ServiceProvider.GetRequiredService<ActivityFeedBroadcaster>();
            broadcaster.Publish(
                new ScraperActivity(
                    Source: "SEC",
                    Severity: ScraperActivitySeverity.Info,
                    Message: "buffered event",
                    Timestamp: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                    CorrelationId: correlationId
                )
            );
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/Status/Activity/Stream");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _host.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token
        );

        response.EnsureSuccessStatusCode();

        var frame = await ReadFrameContaining(response, correlationId, cts.Token);

        frame.Should().Contain("event: activity");
        frame.Should().Contain($"\"CorrelationId\":\"{correlationId}\"");
        frame.Should().Contain("\"Message\":\"buffered event\"");
    }

    private static async Task<string> ReadFrameContaining(
        HttpResponseMessage response,
        string needle,
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
                break;
            if (line.Length == 0)
            {
                if (buffer.Length > 0)
                {
                    var frame = buffer.ToString();
                    if (frame.Contains(needle))
                        return frame;
                    buffer.Clear();
                }
                continue;
            }
            buffer.AppendLine(line);
        }
        return buffer.ToString();
    }
}

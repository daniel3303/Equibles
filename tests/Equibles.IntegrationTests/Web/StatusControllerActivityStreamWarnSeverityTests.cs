using System.Net.Http.Headers;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Services.Activity;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to <see cref="StatusControllerActivityStreamTests"/> (pins LIVE with Info)
/// and <see cref="StatusControllerActivityStreamBacklogTests"/> (pins BACKLOG with Info).
/// Both publish <c>Severity = Info</c>, where the enum's <c>.ToString()</c>
/// happens to equal the <c>[Display(Name = …)]</c> attribute. <c>Warn</c> is
/// the discriminating case: <c>.ToString() = "Warn"</c> but its Display.Name
/// is <c>"Warning"</c>. A regression that switched the payload from
/// <c>activity.Severity.ToString()</c> to the int value or to the
/// Display.Name would silently send <c>"Warning"</c> (or <c>"1"</c>) for the
/// warning row, breaking the browser's badge-color match.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StatusControllerActivityStreamWarnSeverityTests
{
    private readonly WebHostFixture _host;

    public StatusControllerActivityStreamWarnSeverityTests(WebHostFixture host) => _host = host;

    [Fact]
    public async Task ActivityStream_WarnSeverity_EmitsEnumNameNotDisplayName()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Status/Activity/Stream");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _host.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token
        );
        response.EnsureSuccessStatusCode();

        // Unique CorrelationId so the assertion is immune to events left in
        // the shared singleton buffer by other tests in this collection.
        var correlationId = "warn-" + Guid.NewGuid().ToString("N");
        using var scope = _host.Services.CreateScope();
        var broadcaster = scope.ServiceProvider.GetRequiredService<ActivityFeedBroadcaster>();
        broadcaster.Publish(
            new ScraperActivity(
                Source: "SEC",
                Severity: ScraperActivitySeverity.Warn,
                Message: "throttled by upstream",
                Timestamp: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                CorrelationId: correlationId
            )
        );

        var frame = await ReadFrameContaining(response, correlationId, cts.Token);

        frame.Should().Contain("event: activity");
        frame.Should().Contain("\"Severity\":\"Warn\"");
        frame.Should().NotContain("\"Severity\":\"Warning\"");
        frame.Should().NotContain("\"Severity\":1");
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

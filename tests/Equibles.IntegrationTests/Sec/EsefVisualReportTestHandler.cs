using System.Net;
using System.Text;
using Equibles.Integrations.XbrlFilings;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Sec;

// The filing index and the visual reports it states. An issuer with no stated page has no filings; any
// other address is not found, so a request the service composed itself shows up as a failure.
internal sealed class EsefVisualReportTestHandler : HttpMessageHandler
{
    public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
    public List<Uri> Requests { get; } = [];

    public EsefVisualReportText Service() =>
        new(
            new XbrlFilingsClient(new HttpClient(this))
            {
                Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                    1000,
                    TimeSpan.FromSeconds(1)
                ),
            },
            new SecDocumentHtmlNormalizer(),
            new SecDocumentHtmlToMarkdownConverter(),
            NullLogger<EsefVisualReportText>.Instance
        );

    // One issuer's index page holding one filing, with the addresses the host states relative to itself.
    public void StateFiling(
        string lei,
        string periodEnd,
        string jsonPath,
        string reportPath,
        string report
    )
    {
        Bodies[XbrlFilingsClient.EntityIndexUrl(lei, 1, 100).PathAndQuery] =
            "{\"data\":[{\"type\":\"filing\",\"id\":\"1\",\"attributes\":{"
            + $"\"period_end\":\"{periodEnd}\",\"country\":\"BE\",\"json_url\":\"{jsonPath}\","
            + $"\"report_url\":\"{reportPath}\",\"fxo_id\":\"{lei}-{periodEnd}-ESEF-BE-0\","
            + "\"date_added\":\"2026-01-01 00:00:00\",\"error_count\":0},"
            + "\"relationships\":{\"entity\":{\"data\":{\"type\":\"entity\",\"id\":\"9\"}}}}],"
            + "\"included\":[{\"type\":\"entity\",\"id\":\"9\",\"attributes\":{"
            + $"\"identifier\":\"{lei}\",\"name\":\"Issuer\"}}}}],\"meta\":{{\"count\":1}}}}";
        Bodies[reportPath] = report;
    }

    public static string Report(string lei, string periodEnd, string prose) =>
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"><head><title>Report</title></head><body>"
        + "<div style=\"display:none\"><ix:header><ix:resources><xbrli:context id=\"c1\"><xbrli:entity>"
        + $"<xbrli:identifier scheme=\"http://standards.iso.org/iso/17442\">{lei}</xbrli:identifier></xbrli:entity>"
        + $"<xbrli:period><xbrli:instant>{periodEnd}</xbrli:instant></xbrli:period></xbrli:context>"
        + $"</ix:resources></ix:header></div><p>{prose}</p></body></html>";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request.RequestUri);
        string body;
        if (!Bodies.TryGetValue(request.RequestUri.PathAndQuery, out body))
        {
            if (request.RequestUri.AbsolutePath != "/api/filings")
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
                );
            body = "{\"data\":[],\"included\":[],\"meta\":{\"count\":0}}";
        }
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8),
            }
        );
    }
}

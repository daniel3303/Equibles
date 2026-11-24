using System.Text;
using Equibles.Integrations.XbrlFilings;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using static Equibles.UnitTests.Esef.EsefVisualReportTests;

namespace Equibles.UnitTests.Esef;

// The index page is TotalEnergies' recorded eight filings: two records per period (FR and GB), so only the
// exact JSON address picks the report.
public class EsefVisualReportTextTests
{
    private const string Lei = "529900S21EQ1BO4ESM68";
    private const string FrenchJson =
        "/529900S21EQ1BO4ESM68/2022-12-31/ESEF/FR/0/tot-2022-12-31AR.json";
    private const string FrenchReport =
        "/529900S21EQ1BO4ESM68/2022-12-31/ESEF/FR/0/tot-2022-12-31AR/reports/tot-2022-12-31AR.xhtml";
    private static readonly DateOnly End = new(2022, 12, 31);

    [Fact]
    public async Task FromJsonAddress_OneRecordStatesTheAddress_BuildsItsVisualReportText()
    {
        var (service, handler) = Service(report: Report(Context(Lei, "2022-12-31")));

        var text = await service.FromJsonAddress(
            Lei,
            End,
            "https://filings.xbrl.org" + FrenchJson,
            CancellationToken.None
        );

        Encoding.UTF8.GetString(text).Should().Contain("Revenue grew in every market.");
        handler.Requests.Select(uri => uri.AbsolutePath).Should().Contain(FrenchReport);
    }

    [Fact]
    public async Task FromJsonAddress_NoRecordStatesTheAddress_IsEmptyAndReadsNoReport()
    {
        var (service, handler) = Service(report: Report(Context(Lei, "2022-12-31")));

        var text = await service.FromJsonAddress(
            Lei,
            End,
            "https://filings.xbrl.org/elsewhere.json",
            CancellationToken.None
        );

        text.Should().BeEmpty();
        handler.Requests.Should().ContainSingle().Which.AbsolutePath.Should().Be("/api/filings");
    }

    [Fact]
    public async Task FromJsonAddress_TwoRecordsStateTheAddress_Throws()
    {
        var page = JObject.Parse(Page());
        var records = (JArray)page["data"];
        records.Add(
            records
                .First(record => (string)record["attributes"]["json_url"] == FrenchJson)
                .DeepClone()
        );
        var (service, _) = Service(
            report: Report(Context(Lei, "2022-12-31")),
            page: page.ToString()
        );

        var read = () =>
            service.FromJsonAddress(
                Lei,
                End,
                "https://filings.xbrl.org" + FrenchJson,
                CancellationToken.None
            );

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task FromJsonAddress_VisualReportNamesAnotherIssuer_Throws()
    {
        var (service, _) = Service(report: Report(Context("259400NFU8A8SBP6VC21", "2022-12-31")));

        var read = () =>
            service.FromJsonAddress(
                Lei,
                End,
                "https://filings.xbrl.org" + FrenchJson,
                CancellationToken.None
            );

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task FromJsonAddress_VisualReportPastTheCeiling_IsEmpty()
    {
        var (service, handler) = Service(report: Report(Context(Lei, "2022-12-31")));
        handler.OverstatedContentLength = XbrlFilingsClient.MaxReportBytes + 1L;
        handler.OverstatedPaths.Add(FrenchReport);

        var text = await service.FromJsonAddress(
            Lei,
            End,
            "https://filings.xbrl.org" + FrenchJson,
            CancellationToken.None
        );

        text.Should().BeEmpty();
    }

    private static (EsefVisualReportText Service, EsefIndexTestHandler Handler) Service(
        string report,
        string page = null
    )
    {
        var handler = new EsefIndexTestHandler(
            new Dictionary<string, string>
            {
                [XbrlFilingsClient.EntityIndexUrl(Lei, 1, 100).PathAndQuery] = page ?? Page(),
                [FrenchReport] = report,
            }
        );
        var client = new XbrlFilingsClient(new HttpClient(handler))
        {
            Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                1000,
                TimeSpan.FromSeconds(1)
            ),
        };
        return (
            new EsefVisualReportText(
                client,
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter(),
                NullLogger<EsefVisualReportText>.Instance
            ),
            handler
        );
    }

    private static string Page() =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Esef", "filings-one-issuer.json")
        );
}

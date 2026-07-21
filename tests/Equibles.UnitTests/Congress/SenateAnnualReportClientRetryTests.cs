using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientRetryTests
{
    // Contract: the eFD fetch treats HTTP 5xx as transient and retries up to
    // MaxRetries. A single server error on the search response must not be
    // fatal — once a later attempt succeeds the report parses normally, and the
    // search call is issued more than once.
    [Fact]
    public async Task GetAnnualReports_TransientServerErrorOnSearch_RetriesAndParsesReport()
    {
        var reportHtml = File.ReadAllText(FixturePath("senate-annual-slotkin-2024.html"));
        var searchJson = JsonConvert.SerializeObject(
            new
            {
                recordsTotal = 1,
                data = new[]
                {
                    new[]
                    {
                        "Jane",
                        "Doe",
                        "Doe, Jane (Senator)",
                        "<a href=\"/search/view/annual/25bb47de-6695-4a3e-8714-49df656bc5fa/\" target=\"_blank\">Annual Report for CY 2024</a>",
                        "10/10/2025",
                    },
                },
            }
        );
        var session = Substitute.For<ISenateBrowserSession>();
        // First search attempt fails with a transient 500; the retry succeeds.
        session
            .Fetch(
                Arg.Is<string>(u => u.EndsWith("/search/report/data/")),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new SenateFetchResult { Status = 500, Body = "" },
                new SenateFetchResult { Status = 200, Body = searchJson }
            );
        session
            .Fetch(
                Arg.Is<string>(u => u.Contains("/search/view/annual/")),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SenateFetchResult { Status = 200, Body = reportHtml });
        var sut = new SenateAnnualReportClient(
            session,
            Substitute.For<ILogger<SenateAnnualReportClient>>()
        );

        var result = await sut.GetAnnualReports(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Reports.Should().ContainSingle().Which.MemberName.Should().Be("Jane Doe");
        await session
            .Received(2)
            .Fetch(
                Arg.Is<string>(u => u.EndsWith("/search/report/data/")),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName);
}

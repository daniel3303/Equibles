using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// GH-749 discovery firehose. Contract: <c>GetDailyIndex</c> parses the
/// pipe-delimited master index and returns ONLY 13F-HR / 13F-HR/A rows whose
/// CIK is all digits — every preamble/header/separator line, every non-13F
/// form (10-K), and the inactivity notice 13F-NT must be excluded, with the
/// accession number derived from the file-name column. A regression that let
/// 13F-NT, a header row, or a non-digit CIK through would feed garbage
/// accessions into the ingestion sweep.
/// </summary>
public class SecEdgarClientGetDailyIndexTests
{
    [Fact]
    public async Task GetDailyIndex_MixedMasterIndex_ReturnsOnly13FHrRowsWithDigitCik()
    {
        const string body =
            "Description:           Master Index of EDGAR Dissemination Feed by Form Type\n"
            + "Last Data Received:    November 20, 2024\n"
            + "Comment:               anything can appear here | even a pipe\n"
            + " \n"
            + "CIK|Company Name|Form Type|Date Filed|File Name\n"
            + "--------------------------------------------------------------------------------\n"
            + "320193|APPLE INC|10-K|2024-11-20|edgar/data/320193/0000320193-24-000123.txt\n"
            + "1067983|BERKSHIRE HATHAWAY INC|13F-HR|2024-11-20|edgar/data/1067983/0000950123-24-006477.txt\n"
            + "1067983|BERKSHIRE HATHAWAY INC|13F-HR/A|2024-11-20|edgar/data/1067983/0000950123-24-006500.txt\n"
            + "9999999|NOTICE FILER|13F-NT|2024-11-20|edgar/data/9999999/0001111111-24-000001.txt\n"
            + "ABCDEFG|BROKEN CIK|13F-HR|2024-11-20|edgar/data/0/0002222222-24-000002.txt\n";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new StubHandler(body)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var entries = await sut.GetDailyIndex(new DateOnly(2024, 11, 20));

        entries.Should().HaveCount(2);
        entries
            .Should()
            .ContainSingle(e => e.AccessionNumber == "0000950123-24-006477")
            .Which.Should()
            .Match<Equibles.Integrations.Sec.Models.EdgarDailyIndexEntry>(e =>
                e.FormType == "13F-HR" && e.Cik == "1067983"
            );
        entries
            .Should()
            .ContainSingle(e =>
                e.AccessionNumber == "0000950123-24-006500" && e.FormType == "13F-HR/A"
            );
        entries.Should().NotContain(e => e.FormType == "13F-NT" || e.FormType == "10-K");
        entries.Should().NotContain(e => e.Cik == "ABCDEFG");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) }
            );
    }
}

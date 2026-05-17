using System.Net;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="SecEdgarClientGetDailyIndexTests"/>, which
/// only feeds well-formed dates. ParseMasterIndex documents itself as keeping
/// every 13F-HR row with an all-digit CIK; a malformed "Date Filed" column is
/// row-level dirty data, not a reason to drop a real filing. Contract: the row
/// must still be returned, with DateFiled falling back to the swept index date
/// (never default/MinValue — a zero date would misfile the filing downstream).
/// </summary>
public class SecEdgarClientGetDailyIndexMalformedDateTests
{
    [Fact]
    public async Task GetDailyIndex_Valid13FHrRowWithUnparseableDateFiled_KeepsRowDatedToIndexDate()
    {
        var indexDate = new DateOnly(2024, 11, 20);
        const string body =
            "CIK|Company Name|Form Type|Date Filed|File Name\n"
            + "--------------------------------------------------------------------------------\n"
            + "1067983|BERKSHIRE HATHAWAY INC|13F-HR|not-a-real-date|edgar/data/1067983/0000950123-24-006477.txt\n";

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

        var entries = await sut.GetDailyIndex(indexDate);

        entries
            .Should()
            .ContainSingle("a malformed date must not drop an otherwise-valid 13F-HR row")
            .Which.Should()
            .Match<EdgarDailyIndexEntry>(e =>
                e.AccessionNumber == "0000950123-24-006477"
                && e.Cik == "1067983"
                && e.DateFiled == indexDate
            );
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

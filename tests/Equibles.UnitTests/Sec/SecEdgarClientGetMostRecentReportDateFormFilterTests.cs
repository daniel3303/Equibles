using System.Net;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetMostRecentReportDate's contract (previously uncovered): it must
/// return the latest report date among filings of the REQUESTED form only. A
/// regression that dropped the form filter would return the newer 10-Q date;
/// one that mis-sorted would return the older 10-K. Both are caught here.
/// </summary>
public class SecEdgarClientGetMostRecentReportDateFormFilterTests
{
    [Fact]
    public async Task GetMostRecentReportDate_TenK_ReturnsLatestTenKIgnoringNewerTenQ()
    {
        // Two 10-Ks (2022-12-31, 2023-12-31) and a newer 10-Q (2024-06-30).
        // Asking for 10-K must yield the latest 10-K, never the later 10-Q.
        const string submissions =
            "{\"cik\":\"320193\",\"fiscalYearEnd\":\"0930\",\"filings\":{\"recent\":{"
            + "\"accessionNumber\":[\"a1\",\"a2\",\"a3\"],"
            + "\"filingDate\":[\"2023-02-01\",\"2024-08-01\",\"2024-02-01\"],"
            + "\"reportDate\":[\"2022-12-31\",\"2024-06-30\",\"2023-12-31\"],"
            + "\"form\":[\"10-K\",\"10-Q\",\"10-K\"],"
            + "\"primaryDocument\":[\"a.htm\",\"b.htm\",\"c.htm\"],"
            + "\"primaryDocDescription\":[\"10-K\",\"10-Q\",\"10-K\"]}}}";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new SingleResponseHandler(submissions)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var result = await sut.GetMostRecentReportDate("0000320193", DocumentTypeFilter.TenK);

        result.Should().Be(new DateOnly(2023, 12, 31));
    }

    private sealed class SingleResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }
}

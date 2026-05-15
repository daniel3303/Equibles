using System.IO.Compression;
using System.Net;
using Equibles.Integrations.Cftc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Sibling to <see cref="CftcClientDownloadTests"/>, which pins the happy
/// download+parse path. This pins the transient-failure resilience in
/// <c>DownloadWithRetry</c>: a 5xx on the first attempt must be retried, not
/// surfaced. The CFTC host returns sporadic 503s; a regression that dropped
/// the <c>(int)response.StatusCode >= 500</c> retry branch (or stopped looping
/// after the first response) would fail the entire yearly COT import on the
/// first hiccup instead of recovering on the next attempt.
/// </summary>
public class CftcClientDownloadRetryTests
{
    [Fact]
    public async Task DownloadYearlyReport_ServerErrorThenSuccess_RetriesAndParses()
    {
        var csv = string.Join(
            "\n",
            "\"Market_and_Exchange_Names\",\"Report_Date_as_YYYY-MM-DD\","
                + "\"CFTC_Contract_Market_Code\",\"Open_Interest_All\"",
            "\"GOLD - COMMODITY EXCHANGE INC.\",\"2024-12-24\",\"088691\",\"500,000\""
        );
        var zipBytes = BuildZipWith("annual.txt", csv);

        var handler = new FlakyZipHandler(zipBytes);
        var sut = new CftcClient(new HttpClient(handler), Substitute.For<ILogger<CftcClient>>());

        var records = await sut.DownloadYearlyReport(2024);

        // First attempt 503 -> retried; second attempt 200 -> parsed.
        handler.Attempts.Should().Be(2);
        records.Should().ContainSingle();
        records[0].ContractMarketCode.Should().Be("088691");
        records[0].OpenInterest.Should().Be(500_000);
    }

    private static byte[] BuildZipWith(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return stream.ToArray();
    }

    private sealed class FlakyZipHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        public int Attempts { get; private set; }

        public FlakyZipHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            if (Attempts == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                }
            );
        }
    }
}

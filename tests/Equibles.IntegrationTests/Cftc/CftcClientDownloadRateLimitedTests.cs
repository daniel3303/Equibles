using System.IO.Compression;
using System.Net;
using Equibles.Integrations.Cftc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Sibling to <see cref="CftcClientDownloadRetryTests"/> (which pins the 5xx
/// retry branch). This pins the 429 branch of <c>DownloadWithRetry</c>, which
/// was 11/11 lines zero-hit in the local cobertura baseline: a CFTC rate-limit
/// response must pause the shared limiter and retry, not surface. A regression
/// that dropped the <c>TooManyRequests</c> case (or merged it with the generic
/// non-retry path) would fail the whole yearly COT import the first time CFTC
/// throttles the scraper.
/// </summary>
public class CftcClientDownloadRateLimitedTests
{
    [Fact]
    public async Task DownloadYearlyReport_RateLimitedThenSuccess_RetriesAndParses()
    {
        var csv = string.Join(
            "\n",
            "\"Market_and_Exchange_Names\",\"Report_Date_as_YYYY-MM-DD\","
                + "\"CFTC_Contract_Market_Code\",\"Open_Interest_All\"",
            "\"SILVER - COMMODITY EXCHANGE INC.\",\"2024-12-24\",\"084691\",\"250,000\""
        );
        var zipBytes = BuildZipWith("annual.txt", csv);

        var handler = new RateLimitedZipHandler(zipBytes);
        var sut = new CftcClient(new HttpClient(handler), Substitute.For<ILogger<CftcClient>>());

        var records = await sut.DownloadYearlyReport(2024);

        // First attempt 429 -> paused + retried; second attempt 200 -> parsed.
        handler.Attempts.Should().Be(2);
        records.Should().ContainSingle();
        records[0].ContractMarketCode.Should().Be("084691");
        records[0].OpenInterest.Should().Be(250_000);
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

    private sealed class RateLimitedZipHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        public int Attempts { get; private set; }

        public RateLimitedZipHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            if (Attempts == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
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

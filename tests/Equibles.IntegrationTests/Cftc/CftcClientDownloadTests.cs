using System.IO.Compression;
using System.Net;
using Equibles.Integrations.Cftc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Unit-tier <c>CftcClientTests</c> only covers the row-level CSV/parse helpers via
/// reflection. The end-to-end <c>DownloadYearlyReport</c> path — HTTP fetch into
/// <see cref="ZipArchive"/>, header indexing, line-by-line parse — is uncovered.
/// This test serves a real ZIP (built in-memory) over a stub <c>HttpMessageHandler</c>
/// and asserts the parsed record carries the expected primary key columns.
/// </summary>
public class CftcClientDownloadTests
{
    [Fact]
    public async Task DownloadYearlyReport_RealZipWithOneRow_ParsesAndReturnsRecord()
    {
        // Build a minimal but realistic CFTC COT CSV inside a ZIP. The header names
        // must match production strings exactly — a regression that renames any of
        // them would silently null out that column for every row.
        var csv = string.Join(
            "\n",
            "\"Market_and_Exchange_Names\",\"Report_Date_as_YYYY-MM-DD\","
                + "\"CFTC_Contract_Market_Code\",\"Open_Interest_All\"",
            "\"E-MINI S&P 500 - CHICAGO MERCANTILE EXCHANGE\",\"2024-12-24\",\"13874+\",\"2,500,000\""
        );

        var zipBytes = BuildZipWith("annual.txt", csv);
        var handler = new ZipHandler(zipBytes);
        var sut = new CftcClient(new HttpClient(handler), Substitute.For<ILogger<CftcClient>>());

        var records = await sut.DownloadYearlyReport(2024);

        records.Should().ContainSingle();
        var record = records[0];
        record.MarketAndExchangeName.Should().Be("E-MINI S&P 500 - CHICAGO MERCANTILE EXCHANGE");
        record.ReportDate.Should().Be("2024-12-24");
        record.ContractMarketCode.Should().Be("13874+");
        // ParseLong must strip the thousand-separator commas before parsing; a regression
        // that called long.Parse directly would throw on "2,500,000" and the record would
        // come back with a null OpenInterest.
        record.OpenInterest.Should().Be(2_500_000);

        // URL must hit the deacot{year}.zip pattern — drop a "0" or zero-pad and CFTC 404s.
        handler.LastUrl.Should().EndWith("deacot2024.zip");
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

    private sealed class ZipHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        public string LastUrl { get; private set; }

        public ZipHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                }
            );
        }
    }
}

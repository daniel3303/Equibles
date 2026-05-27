using System.IO.Compression;
using System.Net;
using Equibles.Integrations.Cftc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Regression pin for GH-2159 — CftcClient.ParseLine was looking up CSV
/// columns by underscore-style names ("Market_and_Exchange_Names",
/// "Open_Interest_All", etc.) but the deacot{year}.zip CSVs that
/// cftc.gov actually ships use space-separated, parenthesised names
/// ("Market and Exchange Names", "Open Interest (All)", with the
/// quirky leading space in " Total Reportable Positions-Long (All)").
/// Every column lookup returned null, every record landed with null
/// numeric fields, and the downstream persistence dropped every row —
/// CftcPositionReport stayed at 0 rows indefinitely while the worker
/// kept logging successful 200 OK downloads.
///
/// This pin feeds the actual production header format and asserts the
/// parser returns a non-empty list with the primary-key fields and at
/// least one numeric field populated. The existing
/// CftcClientDownloadTests passes the buggy underscore headers (which
/// matches the buggy production lookup); it cannot catch this
/// regression class because both sides agree.
/// </summary>
public class CftcClientDownloadRealCftcHeaderFormatTests
{
    [Fact]
    public async Task DownloadYearlyReport_RealCftcHeaderFormat_ReturnsParsedRecord()
    {
        // Header verbatim from `cftc.gov/files/dea/history/deacot2024.zip` —
        // including the leading space in the 14th column (CFTC source-data
        // quirk) and the (All)/(Old)/(Other) suffix groupings.
        var header = string.Join(
            ",",
            "\"Market and Exchange Names\"",
            "\"As of Date in Form YYMMDD\"",
            "\"As of Date in Form YYYY-MM-DD\"",
            "\"CFTC Contract Market Code\"",
            "\"CFTC Market Code in Initials\"",
            "\"CFTC Region Code\"",
            "\"CFTC Commodity Code\"",
            "\"Open Interest (All)\"",
            "\"Noncommercial Positions-Long (All)\"",
            "\"Noncommercial Positions-Short (All)\"",
            "\"Noncommercial Positions-Spreading (All)\"",
            "\"Commercial Positions-Long (All)\"",
            "\"Commercial Positions-Short (All)\"",
            "\" Total Reportable Positions-Long (All)\"",
            "\"Total Reportable Positions-Short (All)\"",
            "\"Nonreportable Positions-Long (All)\"",
            "\"Nonreportable Positions-Short (All)\""
        );

        var dataRow = string.Join(
            ",",
            "\"WHEAT-SRW - CHICAGO BOARD OF TRADE\"",
            "241231",
            "2024-12-31",
            "001602",
            "CBT",
            "1",
            "001",
            "350000",
            "120000",
            "80000",
            "20000",
            "150000",
            "200000",
            "290000",
            "300000",
            "60000",
            "50000"
        );

        var csv = string.Join("\n", header, dataRow);
        var zipBytes = BuildZipWith("annual.txt", csv);
        var handler = new ZipHandler(zipBytes);
        var sut = new CftcClient(new HttpClient(handler), Substitute.For<ILogger<CftcClient>>());

        var records = await sut.DownloadYearlyReport(2024);

        records.Should().ContainSingle();
        var record = records[0];
        record
            .MarketAndExchangeName.Should()
            .Be(
                "WHEAT-SRW - CHICAGO BOARD OF TRADE",
                "the parser must look up columns by the actual CFTC header strings, not the underscore-style names from the legacy lookup"
            );
        record.ContractMarketCode.Should().Be("001602");
        record.ReportDate.Should().Be("2024-12-31");
        record
            .OpenInterest.Should()
            .Be(
                350000,
                "every numeric column was silently parsing to null under the column-name mismatch"
            );
        // Pins the leading-space quirk in " Total Reportable Positions-Long (All)".
        record.TotalRptLong.Should().Be(290000);
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

        public ZipHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                }
            );
        }
    }
}

using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UglyToad.PdfPig.Writer;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Pins the ingested-filing ledger contract on the House PTR client: filings
/// whose DocID is already recorded are never re-downloaded, a successfully
/// parsed filing is reported back as processed (even with zero transactions),
/// and a missing or unreadable PDF is NOT reported — so it retries on a later
/// cycle instead of being lost.
/// </summary>
public class HouseDisclosureClientLedgerSkipTests
{
    private const string IngestedDocId = "20011111";
    private const string NewDocId = "20022222";

    private static string IndexXml() =>
        "<?xml version=\"1.0\"?><FinancialDisclosure>"
        + "<Member><First>Jane</First><Last>Smith</Last>"
        + $"<FilingType>P</FilingType><DocID>{IngestedDocId}</DocID>"
        + "<FilingDate>2024-01-15</FilingDate><StateDst>CA01</StateDst></Member>"
        + "<Member><First>Bob</First><Last>Jones</Last>"
        + $"<FilingType>P</FilingType><DocID>{NewDocId}</DocID>"
        + "<FilingDate>2024-02-01</FilingDate><StateDst>NY02</StateDst></Member>"
        + "</FinancialDisclosure>";

    // A structurally valid one-page PDF with no text: parses to zero
    // transactions, proving "handled with no items" is still recorded.
    private static byte[] BlankPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(612, 792);
        return builder.Build();
    }

    private static HouseDisclosureClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Substitute.For<ILogger<HouseDisclosureClient>>());

    [Fact]
    public async Task GetRecentTransactions_IngestedDocId_IsNeverDownloadedAgain()
    {
        var handler = new HouseHandler(
            IndexXml(),
            docId => new HttpResponseMessage(HttpStatusCode.NotFound)
        );
        var sut = CreateClient(handler);

        await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new HashSet<string> { IngestedDocId },
            CancellationToken.None
        );

        handler.RequestedPdfDocIds.Should().NotContain(IngestedDocId);
        handler.RequestedPdfDocIds.Should().Contain(NewDocId);
    }

    [Fact]
    public async Task GetRecentTransactions_ParsedPdf_IsReportedProcessedEvenWithZeroTransactions()
    {
        var pdf = BlankPdf();
        var handler = new HouseHandler(
            IndexXml(),
            docId => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pdf),
            }
        );
        var sut = CreateClient(handler);

        var result = await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new HashSet<string> { IngestedDocId },
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.ProcessedFilings.Should().ContainSingle();
        result.ProcessedFilings[0].SourceId.Should().Be(NewDocId);
        result.ProcessedFilings[0].ItemCount.Should().Be(0);
    }

    [Fact]
    public async Task GetRecentTransactions_MissingPdf_IsNotReportedProcessed()
    {
        var handler = new HouseHandler(
            IndexXml(),
            docId => new HttpResponseMessage(HttpStatusCode.NotFound)
        );
        var sut = CreateClient(handler);

        var result = await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.ProcessedFilings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentTransactions_UnreadablePdf_IsNotReportedProcessed()
    {
        var handler = new HouseHandler(
            IndexXml(),
            docId => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not a pdf")),
            }
        );
        var sut = CreateClient(handler);

        var result = await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        result.Transactions.Should().BeEmpty();
        result.ProcessedFilings.Should().BeEmpty();
    }

    private sealed class HouseHandler : HttpMessageHandler
    {
        private readonly byte[] _zip;
        private readonly Func<string, HttpResponseMessage> _pdfResponse;

        public List<string> RequestedPdfDocIds { get; } = [];

        public HouseHandler(string indexXml, Func<string, HttpResponseMessage> pdfResponse)
        {
            _pdfResponse = pdfResponse;
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("2024FD.xml");
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(indexXml);
                s.Write(bytes, 0, bytes.Length);
            }
            _zip = buffer.ToArray();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("FD.zip"))
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_zip),
                    }
                );

            var docId = url.Split('/')[^1].Replace(".pdf", "");
            RequestedPdfDocIds.Add(docId);
            return Task.FromResult(_pdfResponse(docId));
        }
    }
}

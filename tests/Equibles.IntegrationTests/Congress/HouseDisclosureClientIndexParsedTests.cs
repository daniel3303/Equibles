using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// <see cref="HouseDisclosureClientIndexNotFoundTests"/> pins the 404
/// short-circuit. This pins the happy index path: a valid {year}FD.zip with an
/// XML index containing one "P" (PTR) filing in range drives the full parse —
/// ZIP open, XML descendant query, FilingType/date filter, HouseFiling
/// projection — and the per-filing loop, where the PTR PDF 404s so
/// <c>DownloadAndParsePtrPdf</c> returns empty without aborting the year.
/// </summary>
public class HouseDisclosureClientIndexParsedTests
{
    [Fact]
    public async Task GetRecentTransactions_ValidIndexWithPtrFiling_ParsesIndexAndAttemptsPdf()
    {
        const int year = 2024;
        var xml =
            "<?xml version=\"1.0\"?><FinancialDisclosure>"
            + "<Member><Prefix>Hon.</Prefix><First>Jane</First><Last>Smith</Last>"
            + "<FilingType>P</FilingType><DocID>20012345</DocID>"
            + "<FilingDate>2024-01-15</FilingDate><StateDst>CA01</StateDst></Member>"
            // A non-PTR filing that must be filtered out by FilingType != "P".
            + "<Member><First>Bob</First><Last>Jones</Last>"
            + "<FilingType>O</FilingType><DocID>9999</DocID>"
            + "<FilingDate>2024-02-01</FilingDate><StateDst>NY02</StateDst></Member>"
            + "</FinancialDisclosure>";

        var handler = new HouseIndexHandler(year, xml);
        var sut = new HouseDisclosureClient(
            new HttpClient(handler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var transactions = await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            CancellationToken.None
        );

        // The PTR PDF 404s, so no transactions are produced — but the index
        // ZIP must have been read and the PTR filing's PDF must have been
        // requested (proving the parse + per-filing loop ran).
        transactions.Should().BeEmpty();
        handler.IndexRequested.Should().BeTrue("the {year}FD.zip index must be fetched");
        handler
            .PdfRequestedDocId.Should()
            .Be("20012345", "only the in-range PTR filing should trigger a PDF fetch");
    }

    private sealed class HouseIndexHandler : HttpMessageHandler
    {
        private readonly byte[] _zip;

        public bool IndexRequested { get; private set; }
        public string PdfRequestedDocId { get; private set; }

        public HouseIndexHandler(int year, string indexXml)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry($"{year}FD.xml");
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
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("/public_disc/financial-pdfs/"))
            {
                IndexRequested = true;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_zip),
                    }
                );
            }
            if (url.Contains("/public_disc/ptr-pdfs/"))
            {
                PdfRequestedDocId = url.Split('/')[^1].Replace(".pdf", "");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

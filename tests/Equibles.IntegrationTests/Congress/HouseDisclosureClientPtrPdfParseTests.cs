using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// <see cref="HouseDisclosureClientIndexParsedTests"/> 404s the PTR PDF (early
/// return). This serves the PTR PDF as 200 with non-PDF bytes, so the
/// post-NotFound path runs (EnsureSuccess → ReadAsByteArray → ParsePtrPdf) and
/// <c>PdfDocument.Open</c> throws — exercising ParsePtrPdf's catch (log + skip),
/// proving one corrupt PDF can't abort the year.
/// </summary>
public class HouseDisclosureClientPtrPdfParseTests
{
    [Fact]
    public async Task GetRecentTransactions_PtrPdfIsCorrupt_LogsAndContinuesWithoutThrowing()
    {
        const int year = 2024;
        var xml =
            "<?xml version=\"1.0\"?><FinancialDisclosure>"
            + "<Member><Prefix>Hon.</Prefix><First>Jane</First><Last>Smith</Last>"
            + "<FilingType>P</FilingType><DocID>20012345</DocID>"
            + "<FilingDate>2024-01-15</FilingDate><StateDst>CA01</StateDst></Member>"
            + "</FinancialDisclosure>";

        var handler = new CorruptPdfHandler(year, xml);
        var sut = new HouseDisclosureClient(
            new HttpClient(handler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var transactions = await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            CancellationToken.None
        );

        // The PDF is unparseable, so no transactions — but the year completed
        // (the corrupt-PDF catch swallowed it) and the PDF was actually fetched.
        transactions.Should().BeEmpty();
        handler.PdfFetched.Should().BeTrue("the post-NotFound parse path must have run");
    }

    private sealed class CorruptPdfHandler : HttpMessageHandler
    {
        private readonly byte[] _zip;
        public bool PdfFetched { get; private set; }

        public CorruptPdfHandler(int year, string indexXml)
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
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(_zip),
                    }
                );
            }
            // PTR PDF endpoint: 200 with bytes that are not a valid PDF.
            PdfFetched = true;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        Encoding.UTF8.GetBytes("this is definitely not a PDF")
                    ),
                }
            );
        }
    }
}

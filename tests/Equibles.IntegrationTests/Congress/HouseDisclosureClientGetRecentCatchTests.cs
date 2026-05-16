using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Pins <c>GetRecentTransactions</c>'s two error arms: the outer catch (the
/// year's filing-index download/parse fails) and the inner per-filing catch (a
/// single PTR fetch hard-fails). Both must log and let the run continue, not
/// throw.
/// </summary>
public class HouseDisclosureClientGetRecentCatchTests
{
    private static byte[] ZipWithIndex(int year, string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"{year}FD.xml");
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(xml);
            s.Write(bytes, 0, bytes.Length);
        }
        return buffer.ToArray();
    }

    [Fact]
    public async Task GetRecentTransactions_CorruptIndexZip_OuterCatchLogsAndContinues()
    {
        // financial-pdfs returns 200 but the body is not a valid ZIP →
        // ZipArchive ctor throws inside DownloadAndParseFilingIndex → outer catch.
        var handler = new RoutingHandler(
            indexResponse: () =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not a zip archive")),
                },
            ptrResponse: () => new HttpResponseMessage(HttpStatusCode.NotFound)
        );

        var transactions = await new HouseDisclosureClient(
            new HttpClient(handler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        ).GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            CancellationToken.None
        );

        transactions.Should().BeEmpty("the corrupt index is caught and the run completes");
    }

    [Fact]
    public async Task GetRecentTransactions_PtrFetchForbidden_InnerCatchLogsAndContinues()
    {
        const int year = 2024;
        var xml =
            "<?xml version=\"1.0\"?><FinancialDisclosure>"
            + "<Member><Prefix>Hon.</Prefix><First>Jane</First><Last>Smith</Last>"
            + "<FilingType>P</FilingType><DocID>20012345</DocID>"
            + "<FilingDate>2024-01-15</FilingDate><StateDst>CA01</StateDst></Member>"
            + "</FinancialDisclosure>";
        var zip = ZipWithIndex(year, xml);

        // Index OK (one filing) but the PTR PDF 403s → EnsureSuccessStatusCode
        // throws inside DownloadAndParsePtrPdf → the inner per-filing catch.
        var handler = new RoutingHandler(
            indexResponse: () =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) },
            ptrResponse: () => new HttpResponseMessage(HttpStatusCode.Forbidden)
        );

        var transactions = await new HouseDisclosureClient(
            new HttpClient(handler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        ).GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            CancellationToken.None
        );

        transactions.Should().BeEmpty("the forbidden PTR fetch is caught per-filing");
        handler.PtrRequested.Should().BeTrue("the inner loop must have attempted the PTR fetch");
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _index;
        private readonly Func<HttpResponseMessage> _ptr;
        public bool PtrRequested { get; private set; }

        public RoutingHandler(
            Func<HttpResponseMessage> indexResponse,
            Func<HttpResponseMessage> ptrResponse
        )
        {
            _index = indexResponse;
            _ptr = ptrResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("/public_disc/financial-pdfs/"))
                return Task.FromResult(_index());
            PtrRequested = true;
            return Task.FromResult(_ptr());
        }
    }
}

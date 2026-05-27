using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="HouseDisclosureClient"/>. The PTR parser is exercised
/// two ways: at the reconstructed-line level via the internal
/// <see cref="HouseDisclosureClient.ParseTransactionLines"/>, and end-to-end
/// against checked-in real House Clerk PTR PDFs (public-domain federal
/// disclosures) via <see cref="HouseDisclosureClient.ParsePtrPdf(byte[], string, DateOnly)"/>,
/// which proves the page-geometry line reconstruction too. The download flow is
/// exercised via stub <see cref="HttpMessageHandler"/>s.
/// </summary>
public class HouseDisclosureClientTests
{
    private static readonly DateOnly FilingDate = new(2024, 7, 2);

    private static List<DisclosureTransaction> Parse(params string[] lines) =>
        HouseDisclosureClient.ParseTransactionLines(lines, "Nancy Pelosi", FilingDate);

    // ---- reconstructed-line parsing ----

    [Fact]
    public void ParseTransactionLines_SpouseStockWithTickerAndAmountOnWrapLine_ParsesEverything()
    {
        // Real reconstructed shape (House PTR, Pelosi 2024): the owner code and
        // the start of the asset share the row that also carries the type and
        // both dates; the rest of the asset name, the bracketed asset-type code,
        // and the upper amount bound wrap onto the next line — including the
        // ticker itself ("(NVDA)" lands on the wrap line here).
        var result = Parse(
            "SP NVIDIA Corporation - Common P 06/26/2024 06/26/2024 $1,000,001 -",
            "Stock (NVDA) [ST] $5,000,000",
            "F      S     : New",
            "D          : Purchased 10,000 shares."
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("NVDA");
        tx.OwnerType.Should().Be("SP");
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
        tx.TransactionDate.Should().Be(new DateOnly(2024, 6, 26));
        tx.AmountFrom.Should().Be(1_000_001);
        tx.AmountTo.Should().Be(5_000_000);
    }

    [Fact]
    public void ParseTransactionLines_MemberOwnedRowWithBlankOwnerColumn_IsNotDropped()
    {
        // The headline bug (issue #2160): member-owned holdings leave the Owner
        // column blank, so the row does not start with an owner code. The old
        // owner-code-anchored parser dropped every such row — which is the
        // majority of House trades — so no House member that traded only in
        // their own name ever landed in CongressMember. This row must parse with
        // a null owner.
        var result = Parse(
            "Apple Inc. - Common Stock (AAPL) P 03/03/2024 03/05/2024 $1,001 -",
            "$15,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.OwnerType.Should().BeNull("member-owned holdings leave the owner column blank");
        tx.Ticker.Should().Be("AAPL");
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
        tx.AmountFrom.Should().Be(1_001);
        tx.AmountTo.Should().Be(15_000);
    }

    [Fact]
    public void ParseTransactionLines_PartialSaleWithWrappedAmount_ClassifiesAsSale()
    {
        var result = Parse(
            "SP Visa Inc. (V) [ST] S (partial) 07/01/2024 07/01/2024 $500,001 -",
            "$1,000,000",
            "F      S     : New"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().Be("V");
        tx.OwnerType.Should().Be("SP");
        tx.TransactionType.Should().Be(CongressTransactionType.Sale);
        tx.TransactionDate.Should().Be(new DateOnly(2024, 7, 1));
        tx.AmountFrom.Should().Be(500_001);
        tx.AmountTo.Should().Be(1_000_000);
    }

    [Fact]
    public void ParseTransactionLines_AssetTypeCodeButNoParenthesizedTicker_DoesNotMistakeCodeForTicker()
    {
        // A private-fund row (member-owned, blank owner) whose only bracketed
        // token is the asset-type code "[OI]". Without stripping the code first,
        // the ticker extractor would read "OI" — a real NYSE symbol — and
        // fabricate a trade. The row should still parse (so the member lands),
        // but with a null ticker so the downstream tracked-stock filter drops it.
        var result = Parse(
            "New Water Capital Partners II, LP P 10/12/2023 10/13/2023 $982",
            "(GLAS Funds, LP) [OI]",
            "D          : Capital Call for investment"
        );

        var tx = result.Should().ContainSingle().Subject;
        tx.Ticker.Should().BeNull("the bracketed asset-type code is not a ticker");
        tx.OwnerType.Should().BeNull();
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
    }

    [Fact]
    public void ParseTransactionLines_HeaderFieldLabelAndFooterLines_ProduceNoTransactions()
    {
        // None of these lines is a transaction row. The "Digitally Signed … ,
        // 02/23/2024" footer carries a date but no transaction-type anchor and
        // must not be misread as a trade.
        var result = Parse(
            "ID Owner Asset Transaction Date Notification Amount Cap.",
            "F      S     : New",
            "D          : Purchased 10,000 shares.",
            "* For the complete list of asset type abbreviations, please visit https://fd.house.gov/...",
            "Digitally Signed: Hon. Nancy Pelosi , 02/23/2024"
        );

        result.Should().BeEmpty();
    }

    // ---- end-to-end against real PTR PDFs (page-geometry reconstruction) ----

    [Fact]
    public void ParsePtrPdf_RealSpouseStockFiling_ExtractsAllTradedTickers()
    {
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-spouse-stocks.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdf(bytes, "Nancy Pelosi", FilingDate);

        result.Select(t => t.Ticker).Should().BeEquivalentTo(["AVGO", "NVDA", "TSLA", "V"]);

        var nvda = result.Single(t => t.Ticker == "NVDA");
        nvda.OwnerType.Should().Be("SP");
        nvda.TransactionType.Should().Be(CongressTransactionType.Purchase);
        nvda.TransactionDate.Should().Be(new DateOnly(2024, 6, 26));
        nvda.AmountFrom.Should().Be(1_000_001);
        nvda.AmountTo.Should().Be(5_000_000);

        result
            .Single(t => t.Ticker == "TSLA")
            .TransactionType.Should()
            .Be(CongressTransactionType.Sale);
        result
            .Single(t => t.Ticker == "V")
            .TransactionType.Should()
            .Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ParsePtrPdf_RealMemberOwnedFiling_ParsesRowThatHasNoOwnerCode()
    {
        // Regression for #2160: this real PTR is a single member-owned row with
        // a blank owner column. The old parser produced zero transactions for
        // it (and every filing like it), so the member never landed. It must now
        // parse — with a null owner and (being a private fund) a null ticker.
        var bytes = File.ReadAllBytes(FixturePath("house-ptr-own-trade.pdf"));

        var result = HouseDisclosureClient.ParsePtrPdf(bytes, "Max Miller", FilingDate);

        var tx = result.Should().ContainSingle().Subject;
        tx.OwnerType.Should().BeNull();
        tx.Ticker.Should().BeNull();
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName);

    // ---- download flow ----

    [Fact]
    public async Task GetRecentTransactions_FdZipReturns404ForYear_ReturnsEmptyListWithoutThrowing()
    {
        // A 404 FD ZIP is the common case for empty years and must short-circuit
        // quietly in DownloadAndParseFilingIndex rather than throw out of
        // EnsureSuccessStatusCode and cascade into per-filing PDF downloads.
        var handler = new ConstantStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            CancellationToken.None
        );

        result.Should().BeEmpty();
        handler
            .Requests.Should()
            .ContainSingle(
                "only the year's FD ZIP should be requested — a 404 must not cascade into per-filing PDF downloads"
            );
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipMissingExpectedXmlEntry_ReturnsEmptyListAndDoesNotRequestAnyPtrPdf()
    {
        // A 200 ZIP whose {year}FD.xml entry is missing/misnamed must hit the
        // null-entry guard and return [] without dereferencing the entry or
        // cascading into PTR PDF downloads.
        var zipBytes = BuildZipWithSingleEntry("wrongname.xml", "<irrelevant />");
        var handler = new BytesContentHandler(zipBytes, "application/zip");
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            CancellationToken.None
        );

        result.Should().BeEmpty();
        handler
            .Requests.Should()
            .ContainSingle(
                "the missing-XML-entry branch must not cascade into per-filing PTR PDF downloads"
            );
        handler.Requests[0].Should().Contain("2025FD.zip");
    }

    [Fact]
    public void ParsePtrPdf_MalformedPdfBytes_ReturnsEmptyListWithoutThrowing()
    {
        // The per-filing ParsePtrPdf(byte[], HouseFiling) wrapper scopes a bad
        // PDF (truncated CDN response, corrupt upload) to a single skipped
        // filing instead of letting PdfDocument.Open's exception abort the
        // remaining year of filings in GetRecentTransactions.
        var parsePtrPdfMethod = typeof(HouseDisclosureClient).GetMethod(
            "ParsePtrPdf",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var houseFilingType = typeof(HouseDisclosureClient).GetNestedType(
            "HouseFiling",
            BindingFlags.NonPublic
        );
        var filing = houseFilingType
            .GetConstructors()[0]
            .Invoke(["Jane Doe", "20251234", new DateOnly(2025, 2, 1), "CA01"]);
        using var httpClient = new HttpClient();
        var client = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );
        var malformedBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result =
            (List<DisclosureTransaction>)parsePtrPdfMethod.Invoke(client, [malformedBytes, filing]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipContainsOneMatchingFilingButPtrPdfIs404_EntersPerFilingForeachAndExitsCleanly()
    {
        const int year = 2025;
        const string docId = "20251234";
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <Prefix>Hon.</Prefix>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <Year>{year}</Year>
                <FilingDate>2/1/{year}</FilingDate>
                <DocID>{docId}</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(
            httpClient,
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            CancellationToken.None
        );

        result.Should().BeEmpty();
        handler
            .Requests.Should()
            .HaveCount(
                2,
                "one for the FD ZIP (with one matching filing inside) and one for the per-filing PtR PDF"
            );
        handler.Requests[0].Should().Contain($"{year}FD.zip");
        handler.Requests[1].Should().Contain($"ptr-pdfs/{year}/{docId}.pdf");
    }

    private static byte[] BuildZipWithSingleEntry(string entryName, string entryContent)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(entryContent);
        }
        return memory.ToArray();
    }

    private sealed class ConstantStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public List<string> Requests { get; } = new();

        public ConstantStatusHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class BytesContentHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly string _contentType;
        public List<string> Requests { get; } = new();

        public BytesContentHandler(byte[] content, string contentType)
        {
            _content = content;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
            return Task.FromResult(response);
        }
    }

    private sealed class UrlRoutingHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        public List<string> Requests { get; } = new();

        public UrlRoutingHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);

            if (url.Contains("/financial-pdfs/") && url.EndsWith("FD.zip"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_zipBytes),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                return Task.FromResult(response);
            }

            if (url.Contains("/ptr-pdfs/"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}

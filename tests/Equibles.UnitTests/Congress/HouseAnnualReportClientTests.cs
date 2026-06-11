using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static Equibles.Congress.HostedService.Services.HouseAnnualReportClient;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="HouseAnnualReportClient"/>. The Form A parser is
/// exercised two ways: end-to-end against checked-in real House Clerk annual
/// report PDFs (public-domain federal disclosures) via
/// <see cref="HouseAnnualReportClient.ParseAnnualReportPdf"/>, which proves
/// the page-geometry token reconstruction, and at the token level via
/// <see cref="HouseAnnualReportClient.ParseScheduleLines"/> for the wrap and
/// label-paragraph corner cases. The download flow runs against stub
/// <see cref="HttpMessageHandler"/>s.
/// </summary>
public class HouseAnnualReportClientTests
{
    // ---- end-to-end against real annual report PDFs ----

    [Fact]
    public void ParseAnnualReportPdf_RealLargeFiling_ParsesAssetAndLiabilityRows()
    {
        // Pelosi 2024 (DocID 10066169): 63 Schedule A rows of which 4 carry no
        // dollar range ("None" / "Undetermined" values), and 9 Schedule D rows
        // crossing a page break without a repeated column header.
        var bytes = File.ReadAllBytes(FixturePath("house-annual-pelosi-2024.pdf"));

        var content = HouseAnnualReportClient.ParseAnnualReportPdf(bytes);

        content.Should().NotBeNull();
        content.FilerStatus.Should().Be("Member");
        var lines = content.Lines;
        lines.Count(l => l.Kind == CongressionalDisclosureLineKind.Asset).Should().Be(59);
        lines.Count(l => l.Kind == CongressionalDisclosureLineKind.Liability).Should().Be(9);

        var apple = lines.Single(l => l.Description == "Apple Inc. (AAPL)");
        apple.Kind.Should().Be(CongressionalDisclosureLineKind.Asset);
        apple.RangeMinimum.Should().Be(25_000_001);
        apple.RangeMaximum.Should().Be(50_000_000);

        // The asset name wraps onto a second visual line ("… Money Market /
        // Account [BA]") and must be reassembled with the type code stripped.
        var wrapped = lines.Single(l =>
            l.Description == "City National Securities - Brokerage Money Market Account"
        );
        wrapped.RangeMinimum.Should().Be(1_001);
        wrapped.RangeMaximum.Should().Be(15_000);

        // Member-owned rows leave the Owner column blank and must still parse.
        var memberOwned = lines.Single(l =>
            l.Description == "Congressional Credit Union - Checking Account"
        );
        memberOwned.RangeMinimum.Should().Be(50_001);
        memberOwned.RangeMaximum.Should().Be(100_000);

        // This liability is the page-9 continuation of Schedule D — the page
        // break repeats no column header, so the layout must persist.
        var margin = lines.Single(l =>
            l.Description == "Brokerage Margin Account (City National Securities)"
        );
        margin.Kind.Should().Be(CongressionalDisclosureLineKind.Liability);
        margin.RangeMinimum.Should().Be(25_000_001);
        margin.RangeMaximum.Should().Be(50_000_000);

        // The row's "Description:" paragraph wraps onto label-less lines that
        // must not bleed into the asset name.
        lines.Should().Contain(l => l.Description == "AllianceBernstein Holding L.P. Units (AB)");

        // Rows whose value column is "None" or "Undetermined" carry no range
        // and are never materialized.
        lines.Should().NotContain(l => l.Description.Contains("Tesla"));
        lines.Should().NotContain(l => l.Description.Contains("Art of Power"));
        lines.Should().NotContain(l => l.Description.StartsWith("U.S. Bank - Money Market"));
    }

    [Fact]
    public void ParseAnnualReportPdf_RealSmallFiling_ParsesExactRows()
    {
        // Ocasio-Cortez 2024 (DocID 10066093): every asset row is member-owned
        // (blank Owner column), one liability whose Date Incurred cell wraps
        // ("August 2007 - May / 2011") — date text must stay out of the
        // description, and the creditor must fold in.
        var bytes = File.ReadAllBytes(FixturePath("house-annual-aoc-2024.pdf"));

        var content = HouseAnnualReportClient.ParseAnnualReportPdf(bytes);

        content.Should().NotBeNull();
        content.FilerStatus.Should().Be("Member");
        content
            .Lines.Select(l => (l.Kind, l.Description, l.RangeMinimum, l.RangeMaximum))
            .Should()
            .BeEquivalentTo([
                (
                    CongressionalDisclosureLineKind.Asset,
                    "Allied Bank Savings Account",
                    15_001L,
                    50_000L
                ),
                (
                    CongressionalDisclosureLineKind.Asset,
                    "Charles Schwab Bank Checking",
                    1_001L,
                    15_000L
                ),
                (CongressionalDisclosureLineKind.Asset, "Charles Schwab One Brokerage", 1L, 1_000L),
                (
                    CongressionalDisclosureLineKind.Asset,
                    "National Hispanic Institute Inc 401k Plan ⇒ PRUDENTIAL HIGH YIELD Z",
                    1_001L,
                    15_000L
                ),
                (
                    CongressionalDisclosureLineKind.Liability,
                    "Student Loans (U.S. Dept. of Education)",
                    15_001L,
                    50_000L
                ),
            ]);
    }

    [Fact]
    public void ParseAnnualReportPdf_TextlessPdf_ReturnsNull()
    {
        // Scanned paper filings are image-only: no extractable words, so no
        // Schedule A header — the electronic-only policy reports them as null,
        // never as an empty (zero net worth) report.
        var content = HouseAnnualReportClient.ParseAnnualReportPdf(BuildTextlessPdf());

        content.Should().BeNull();
    }

    // ---- token-level schedule parsing ----

    private static List<ScheduleToken> Tokens(params (string text, double left)[] words) =>
        words.Select(w => new ScheduleToken(w.text, w.left)).ToList();

    private static List<ScheduleToken> AssetColumnHeader() =>
        Tokens(
            ("Asset", 25),
            ("Owner", 241),
            ("Value", 280),
            ("of", 311),
            ("Asset", 323),
            ("Income", 363),
            ("Type(s)", 402),
            ("Income", 445)
        );

    [Fact]
    public void ParseScheduleLines_NoAssetScheduleHeader_ReturnsNull()
    {
        var result = HouseAnnualReportClient.ParseScheduleLines([
            Tokens(("Name:", 25), ("Jane", 60), ("Doe", 85)),
        ]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseScheduleLines_WrappedUpperBoundAndAssetName_CompletesTheRow()
    {
        var result = HouseAnnualReportClient.ParseScheduleLines([
            Tokens(("S", 22), ("A:", 92)),
            AssetColumnHeader(),
            Tokens(("Money", 25), ("Market", 60), ("SP", 241), ("$1,000,001", 280), ("-", 329)),
            Tokens(("Account", 25), ("[BA]", 60), ("$5,000,000", 280)),
        ]);

        var line = result.Should().ContainSingle().Subject;
        line.Kind.Should().Be(CongressionalDisclosureLineKind.Asset);
        line.Description.Should().Be("Money Market Account");
        line.RangeMinimum.Should().Be(1_000_001);
        line.RangeMaximum.Should().Be(5_000_000);
    }

    [Fact]
    public void ParseScheduleLines_LabelParagraphProse_DoesNotBleedIntoTheRow()
    {
        // The "Description:" label renders in small caps (extracted as a bare
        // "D :" token); its paragraph wraps across the full page width, so a
        // wrap line can drop words — even dollar amounts — into the value
        // window. Neither the prose nor its amounts may touch the parsed row.
        var result = HouseAnnualReportClient.ParseScheduleLines([
            Tokens(("S", 22), ("A:", 92)),
            AssetColumnHeader(),
            Tokens(
                ("NVIDIA", 25),
                ("[OP]", 70),
                ("SP", 241),
                ("$15,001", 280),
                ("-", 320),
                ("$50,000", 325)
            ),
            Tokens(("D :", 25), ("Purchased", 80), ("50", 130), ("call", 145), ("options", 160)),
            Tokens(
                ("with", 25),
                ("a", 50),
                ("strike", 60),
                ("price", 90),
                ("of", 120),
                ("$200", 280),
                ("and", 310)
            ),
            Tokens(("12/20/24.", 25)),
        ]);

        var line = result.Should().ContainSingle().Subject;
        line.Description.Should().Be("NVIDIA");
        line.RangeMinimum.Should().Be(15_001);
        line.RangeMaximum.Should().Be(50_000);
    }

    [Fact]
    public void ParseScheduleLines_OpenTopBracket_ParsesAsOpenEndedRange()
    {
        var result = HouseAnnualReportClient.ParseScheduleLines([
            Tokens(("S", 22), ("A:", 92)),
            AssetColumnHeader(),
            Tokens(("Family", 25), ("Trust", 55), ("SP", 241), ("Over", 280), ("$50,000,000", 302)),
        ]);

        var line = result.Should().ContainSingle().Subject;
        line.RangeMinimum.Should().Be(50_000_000);
        line.RangeMaximum.Should().Be(50_000_000);
    }

    [Fact]
    public void ParseScheduleLines_NoneAndUndeterminedValues_ProduceNoLineItems()
    {
        var result = HouseAnnualReportClient.ParseScheduleLines([
            Tokens(("S", 22), ("A:", 92)),
            AssetColumnHeader(),
            Tokens(("Book", 25), ("Contract", 50), ("Undetermined", 280)),
            Tokens(("Options", 25), ("SP", 241), ("None", 280), ("None", 363)),
        ]);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // ---- download flow ----

    [Fact]
    public async Task GetAnnualReports_FdZipReturns404_ReturnsEmptyListWithoutThrowing()
    {
        var handler = new ConstantStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseAnnualReportClient(
            httpClient,
            Substitute.For<ILogger<HouseAnnualReportClient>>()
        );

        var result = await sut.GetAnnualReports(2024, CancellationToken.None);

        result.Should().BeEmpty();
        handler
            .Requests.Should()
            .ContainSingle("a 404 FD ZIP must not cascade into per-filing PDF downloads");
    }

    [Fact]
    public async Task GetAnnualReports_IndexWithMixedFilingTypes_FetchesOnlyAnnualReportDocs()
    {
        // The index mixes PTRs ("P"), candidate reports ("C") and extensions
        // ("X") with annual originals ("O") and amendments ("A") — only the
        // last two are annual report documents.
        const int year = 2024;
        var xml = $"""
            <FinancialDisclosures>
              {IndexMember("Jane", "Doe", "O", "5/15/{0}", "10000001", year)}
              {IndexMember("Jane", "Doe", "P", "2/1/{0}", "20000001", year)}
              {IndexMember("John", "Roe", "A", "8/2/{0}", "10000002", year)}
              {IndexMember("Jim", "Poe", "C", "3/3/{0}", "10000003", year)}
              {IndexMember("Joe", "Moe", "X", "4/4/{0}", "30000001", year)}
            </FinancialDisclosures>
            """;
        var handler = new UrlRoutingHandler(BuildZipWithSingleEntry($"{year}FD.xml", xml), null);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseAnnualReportClient(
            httpClient,
            Substitute.For<ILogger<HouseAnnualReportClient>>()
        );

        var result = await sut.GetAnnualReports(year, CancellationToken.None);

        result.Should().BeEmpty("every annual PDF request 404s in this setup");
        var pdfRequests = handler.Requests.Where(r => r.EndsWith(".pdf")).ToList();
        pdfRequests
            .Should()
            .BeEquivalentTo([
                $"https://disclosures-clerk.house.gov/public_disc/financial-pdfs/{year}/10000001.pdf",
                $"https://disclosures-clerk.house.gov/public_disc/financial-pdfs/{year}/10000002.pdf",
            ]);
    }

    [Fact]
    public async Task GetAnnualReports_ElectronicFiling_MapsReportFieldsAndParsesLines()
    {
        const int year = 2024;
        var xml = $"""
            <FinancialDisclosures>
              {IndexMember("Jane Q.", "Doe", "A", "8/20/{0}", "10000009", year)}
            </FinancialDisclosures>
            """;
        var pdfBytes = File.ReadAllBytes(FixturePath("house-annual-aoc-2024.pdf"));
        var handler = new UrlRoutingHandler(
            BuildZipWithSingleEntry($"{year}FD.xml", xml),
            pdfBytes
        );
        using var httpClient = new HttpClient(handler);
        var sut = new HouseAnnualReportClient(
            httpClient,
            Substitute.For<ILogger<HouseAnnualReportClient>>()
        );

        var result = await sut.GetAnnualReports(year, CancellationToken.None);

        var report = result.Should().ContainSingle().Subject;
        report.MemberName.Should().Be("Jane Q. Doe", "the honorific prefix is stripped");
        report.Position.Should().Be(CongressPosition.Representative);
        report.StateDistrict.Should().Be("NY14");
        report.Year.Should().Be(year);
        report.FiledDate.Should().Be(new DateOnly(year, 8, 20));
        report.ReportId.Should().Be("10000009");
        report.IsAmendment.Should().BeTrue();
        report.Lines.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetAnnualReports_ScannedPaperFiling_IsSkippedAsNonElectronic()
    {
        const int year = 2024;
        var xml = $"""
            <FinancialDisclosures>
              {IndexMember("Jane", "Doe", "O", "5/15/{0}", "9000001", year)}
            </FinancialDisclosures>
            """;
        var handler = new UrlRoutingHandler(
            BuildZipWithSingleEntry($"{year}FD.xml", xml),
            BuildTextlessPdf()
        );
        using var httpClient = new HttpClient(handler);
        var sut = new HouseAnnualReportClient(
            httpClient,
            Substitute.For<ILogger<HouseAnnualReportClient>>()
        );

        var result = await sut.GetAnnualReports(year, CancellationToken.None);

        result
            .Should()
            .BeEmpty("an image-only PDF has no schedules and is not an electronic filing");
        handler.Requests.Should().HaveCount(2, "the ZIP and the single PDF");
    }

    // ---- helpers ----

    private static string IndexMember(
        string first,
        string last,
        string filingType,
        string filingDateTemplate,
        string docId,
        int year
    ) =>
        $"""
            <Member>
                <Prefix>Hon.</Prefix>
                <First>{first}</First>
                <Last>{last}</Last>
                <FilingType>{filingType}</FilingType>
                <StateDst>NY14</StateDst>
                <Year>{year}</Year>
                <FilingDate>{string.Format(filingDateTemplate, year)}</FilingDate>
                <DocID>{docId}</DocID>
            </Member>
            """;

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName);

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

    // A minimal valid one-page PDF with no content stream at all — the same
    // "no extractable words" signature a scanned paper filing produces.
    private static byte[] BuildTextlessPdf()
    {
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        void Append(string objectText)
        {
            offsets.Add(builder.Length);
            builder.Append(objectText);
        }

        Append("1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n");
        Append("2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n");
        Append("3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>\nendobj\n");

        var xrefPosition = builder.Length;
        builder.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            builder.Append($"{offset:D10} 00000 n \n");
        builder.Append($"trailer\n<</Size 4/Root 1 0 R>>\nstartxref\n{xrefPosition}\n%%EOF");

        return Encoding.ASCII.GetBytes(builder.ToString());
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

    private sealed class UrlRoutingHandler : HttpMessageHandler
    {
        private readonly byte[] _zipBytes;
        private readonly byte[] _pdfBytes;
        public List<string> Requests { get; } = new();

        public UrlRoutingHandler(byte[] zipBytes, byte[] pdfBytes)
        {
            _zipBytes = zipBytes;
            _pdfBytes = pdfBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);

            if (url.EndsWith("FD.zip"))
                return Task.FromResult(BytesResponse(_zipBytes, "application/zip"));

            if (url.EndsWith(".pdf") && _pdfBytes != null)
                return Task.FromResult(BytesResponse(_pdfBytes, "application/pdf"));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage BytesResponse(byte[] content, string contentType)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return response;
        }
    }
}

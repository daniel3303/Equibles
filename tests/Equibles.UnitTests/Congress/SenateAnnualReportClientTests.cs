using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="SenateAnnualReportClient"/>. The annual report parser
/// runs end-to-end against checked-in real eFD report pages (public-domain
/// federal disclosures); the search/fetch flow runs against a stub
/// <see cref="ISenateBrowserSession"/>.
/// </summary>
public class SenateAnnualReportClientTests
{
    // ---- report HTML parsing against real eFD pages ----

    [Fact]
    public void ParseAnnualReportHtml_RealLargeFiling_KeepsOnlyRowsWithDisclosedRanges()
    {
        // Blackburn CY 2024 (Amendment 1): 41 Part 3 rows of which 9 carry the
        // "--" sentinel and 1 reads "Unascertainable" — only the 31 rows with
        // a real bracket may materialize. One Part 7 liability.
        var html = File.ReadAllText(FixturePath("senate-annual-blackburn-2024.html"));

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().NotBeNull();
        lines.Count(l => l.Kind == CongressionalDisclosureLineKind.Asset).Should().Be(31);

        var payflex = lines.Single(l => l.Description == "Payflex Systems USA INC");
        payflex.RangeMinimum.Should().Be(15_001);
        payflex.RangeMaximum.Should().Be(50_000);

        var liability = lines
            .Should()
            .ContainSingle(l => l.Kind == CongressionalDisclosureLineKind.Liability)
            .Subject;
        liability.Description.Should().Be("Mortgage (JPMorgan Chase Bank, NA)");
        liability.RangeMinimum.Should().Be(250_001);
        liability.RangeMaximum.Should().Be(500_000);

        // "Unascertainable" is not a bracket and must never be emitted.
        lines
            .Should()
            .NotContain(l => l.Description.StartsWith("Principal Investment Plus Variable"));
    }

    [Fact]
    public void ParseAnnualReportHtml_RealSmallFiling_ParsesExactRows()
    {
        // Slotkin CY 2024 (Amendment 1): every asset row has a bracket; the
        // liability's creditor cell nests its location in a ".muted" div that
        // must stay out of the description.
        var html = File.ReadAllText(FixturePath("senate-annual-slotkin-2024.html"));

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().NotBeNull();
        lines
            .Select(l => (l.Kind, l.Description, l.RangeMinimum, l.RangeMaximum))
            .Should()
            .BeEquivalentTo([
                (CongressionalDisclosureLineKind.Asset, "Comerica Bank", 250_001L, 500_000L),
                (CongressionalDisclosureLineKind.Asset, "Comerica Bank", 1_001L, 15_000L),
                (
                    CongressionalDisclosureLineKind.Asset,
                    "International Business Machines Corporation (IBM)",
                    15_001L,
                    50_000L
                ),
                (CongressionalDisclosureLineKind.Asset, "PNC Bank", 15_001L, 50_000L),
                (
                    CongressionalDisclosureLineKind.Asset,
                    "KD - Kyndryl Holdings, Inc. Common Stock",
                    1_001L,
                    15_000L
                ),
                (CongressionalDisclosureLineKind.Asset, "PNC Bank", 1_001L, 15_000L),
                (
                    CongressionalDisclosureLineKind.Liability,
                    "Mortgage (Bank of America)",
                    100_001L,
                    250_000L
                ),
            ]);
    }

    [Fact]
    public void ParseAnnualReportHtml_PageWithoutAssetsHeading_ReturnsNull()
    {
        // An unrecognized layout (error page, redesign) must surface as null —
        // never as a zero-asset report.
        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(
            "<html><body><h1>Something else entirely</h1></body></html>"
        );

        lines.Should().BeNull();
    }

    // ---- search row parsing ----

    private static List<string> SearchRow(string link) =>
        ["Jane", "Doe", "Doe, Jane (Senator)", link, "08/20/2025"];

    [Fact]
    public void ParseSearchRow_AnnualReportWithAmendment_MapsYearAndAmendment()
    {
        var filing = SenateAnnualReportClient.ParseSearchRow(
            SearchRow(
                "<a href=\"/search/view/annual/8c41ca4c-ccda-483e-99ed-d7f27f8a5c8f/\" target=\"_blank\">Annual Report for CY 2024 (Amendment 1)</a>"
            )
        );

        filing.Should().NotBeNull();
        filing.MemberName.Should().Be("Jane Doe");
        filing.Year.Should().Be(2024);
        filing.IsAmendment.Should().BeTrue();
        filing.DateSubmitted.Should().Be(new DateOnly(2025, 8, 20));
        filing
            .ReportUrl.Should()
            .Be(
                "https://efdsearch.senate.gov/search/view/annual/8c41ca4c-ccda-483e-99ed-d7f27f8a5c8f/"
            );
        DisclosureParsingHelper
            .ExtractReportId(filing.ReportUrl)
            .Should()
            .Be("8c41ca4c-ccda-483e-99ed-d7f27f8a5c8f");
    }

    [Fact]
    public void ParseSearchRow_CandidateReportLinkText_IsNotAnAnnualReport()
    {
        // Candidate reports share the eFD annual layout and report-type filter
        // but are not senator annual reports — the link text is the tell.
        var filing = SenateAnnualReportClient.ParseSearchRow(
            SearchRow(
                "<a href=\"/search/view/annual/54fca62e-0132-48a4-b315-40dbc1890b13/\" target=\"_blank\">Candidate Report </a>"
            )
        );

        filing.Should().BeNull();
    }

    [Fact]
    public void ParseSearchRow_PaperFiling_IsSkippedAsNonElectronic()
    {
        var filing = SenateAnnualReportClient.ParseSearchRow(
            SearchRow(
                "<a href=\"/search/view/paper/0b1463c4-2f01-4d21-9a4a-b07d654c0f43/\" target=\"_blank\">Annual Report for CY 2024</a>"
            )
        );

        filing.Should().BeNull();
    }

    // ---- search + fetch flow against a stub browser session ----

    [Fact]
    public async Task GetAnnualReports_ElectronicFiling_MapsReportFieldsAndParsesLines()
    {
        var reportHtml = File.ReadAllText(FixturePath("senate-annual-slotkin-2024.html"));
        var searchJson = JsonConvert.SerializeObject(
            new
            {
                recordsTotal = 1,
                data = new[]
                {
                    new[]
                    {
                        "Jane",
                        "Doe",
                        "Doe, Jane (Senator)",
                        "<a href=\"/search/view/annual/25bb47de-6695-4a3e-8714-49df656bc5fa/\" target=\"_blank\">Annual Report for CY 2024 (Amendment 1)</a>",
                        "10/10/2025",
                    },
                },
            }
        );
        var session = Substitute.For<ISenateBrowserSession>();
        session
            .Fetch(
                Arg.Is<string>(u => u.EndsWith("/search/report/data/")),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SenateFetchResult { Status = 200, Body = searchJson });
        session
            .Fetch(
                Arg.Is<string>(u => u.Contains("/search/view/annual/")),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SenateFetchResult { Status = 200, Body = reportHtml });
        var sut = new SenateAnnualReportClient(
            session,
            Substitute.For<ILogger<SenateAnnualReportClient>>()
        );

        var result = await sut.GetAnnualReports(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            new HashSet<string>(),
            CancellationToken.None
        );

        var report = result.Reports.Should().ContainSingle().Subject;
        report.MemberName.Should().Be("Jane Doe");
        report.Position.Should().Be(CongressPosition.Senator);
        report.Year.Should().Be(2024);
        report.FiledDate.Should().Be(new DateOnly(2025, 10, 10));
        report.ReportId.Should().Be("25bb47de-6695-4a3e-8714-49df656bc5fa");
        report.IsAmendment.Should().BeTrue();
        report.Lines.Should().HaveCount(7);
        await session.Received(1).EnsureAuthenticated(Arg.Any<CancellationToken>());
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName);
}

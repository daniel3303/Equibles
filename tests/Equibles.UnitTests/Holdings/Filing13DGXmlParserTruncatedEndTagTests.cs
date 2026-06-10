using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay: a real EDGAR-accepted Schedule 13D/A (Strome's 2025-01-07 amendment
// on Zivo Bioscience) whose primary_doc.xml is not well-formed — line 154 ends with
// a truncated end tag (`</fundsSource` with no closing `>`), a recurring defect of
// some filer software. EDGAR accepted the filing, so the parser must tolerate it
// instead of throwing XmlException and permanently dropping the filing.
public class Filing13DGXmlParserTruncatedEndTagTests
{
    private readonly Filing13DGXmlParser _sut = new();

    private static string LoadCassette() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings13DG",
                "sc13da-zivo-truncated-endtag.xml"
            )
        );

    [Fact]
    public void ParseFiling_TruncatedEndTag_StillExtractsCoverPageAndReportingPersons()
    {
        var result = _sut.ParseFiling(
            LoadCassette(),
            accessionNumber: "0001213900-25-001739",
            cik: "0001713153",
            filingDate: new DateOnly(2025, 1, 7)
        );

        result.SubmissionType.Should().Be("SCHEDULE 13D/A");
        result.IsAmendment.Should().BeTrue();
        result.FilerCik.Should().Be("1713153");
        result.DateOfEvent.Should().Be(new DateOnly(2024, 12, 26));
        result.IssuerCik.Should().Be("1101026");
        result.IssuerCusip.Should().Be("98978N101");
        result.IssuerName.Should().Be("Zivo Bioscience, Inc.");
        result.ReportingPersons.Should().HaveCount(5);
    }
}

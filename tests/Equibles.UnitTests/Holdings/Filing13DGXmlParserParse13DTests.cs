using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay: drives the parser through a real captured Schedule 13D
// primary_doc.xml (Affinity Partners' 2025-05-06 13D on QXO, Inc.). Frozen
// input, so exact values are asserted — any drift means the parser regressed.
public class Filing13DGXmlParserParse13DTests
{
    private readonly Filing13DGXmlParser _sut = new();

    private static string LoadCassette() =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", "sc13d-qxo.xml")
        );

    [Fact]
    public void ParseFiling_Real13D_ExtractsCoverPageAndAllReportingPersons()
    {
        var result = _sut.ParseFiling(
            LoadCassette(),
            accessionNumber: "0001140361-25-017533",
            cik: "0002059583",
            filingDate: new DateOnly(2025, 5, 6)
        );

        result.SubmissionType.Should().Be("SCHEDULE 13D");
        result.FilingType.Should().Be(FilingType.Schedule13D);
        result.IsAmendment.Should().BeFalse();
        result.FilerCik.Should().Be("2059583");
        result.DateOfEvent.Should().Be(new DateOnly(2025, 4, 29));
        result.IssuerCik.Should().Be("1236275");
        result.IssuerCusip.Should().Be("82846H405");
        result.IssuerName.Should().Be("QXO, Inc.");
        result.SecuritiesClassTitle.Should().Be("Common Stock, par value $0.00001 per share");
        result.ReportingPersons.Should().HaveCount(6);
    }

    [Fact]
    public void ParseFiling_Real13D_ExtractsFirstReportingPersonWithNoCik()
    {
        var person = _sut.ParseFiling(
            LoadCassette(),
            "0001140361-25-017533",
            "0002059583",
            default
        ).ReportingPersons[0];

        person.Name.Should().Be("Affinity Partners Fund I LP");
        person.Cik.Should().BeNull();
        person.SoleVotingPower.Should().Be(0);
        person.SharedVotingPower.Should().Be(164310);
        person.SoleDispositivePower.Should().Be(0);
        person.SharedDispositivePower.Should().Be(164310);
        person.AggregateAmountOwned.Should().Be(164310);
        person.PercentOfClass.Should().Be(0.03m);
        person.TypeOfReportingPerson.Should().Be("PN");
        person.CitizenshipOrOrganization.Should().Be("DE");
    }

    [Fact]
    public void ParseFiling_Real13D_CapturesTheReportingPersonThatCarriesACik()
    {
        var generalPartner = _sut.ParseFiling(
                LoadCassette(),
                "0001140361-25-017533",
                "0002059583",
                default
            )
            .ReportingPersons.Single(p => p.Cik != null);

        generalPartner.Cik.Should().Be("2059583");
        generalPartner.Name.Should().Be("Affinity Partners GP LP");
        generalPartner.AggregateAmountOwned.Should().Be(32671542);
        generalPartner.PercentOfClass.Should().Be(6.3m);
    }
}

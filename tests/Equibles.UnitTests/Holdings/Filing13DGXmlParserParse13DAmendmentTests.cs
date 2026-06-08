using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay against a real Schedule 13D/A amendment (Abu Dhabi Investment
// Authority's 2025-05-06 13D/A). Confirms the amendment marker is detected, an
// amendment still maps to its base FilingType, decimal share amounts truncate,
// and a placeholder issuer CUSIP is carried through verbatim (the import stage,
// not the parser, decides whether an unknown CUSIP maps to a tracked stock).
public class Filing13DGXmlParserParse13DAmendmentTests
{
    private readonly Filing13DGXmlParser _sut = new();

    private static string LoadCassette() =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", "sc13da-adia.xml")
        );

    [Fact]
    public void ParseFiling_Real13DAmendment_FlagsAmendmentAndMapsToBaseType()
    {
        var result = _sut.ParseFiling(
            LoadCassette(),
            accessionNumber: "0001011438-25-000254",
            cik: "0001362558",
            filingDate: new DateOnly(2025, 5, 6)
        );

        result.SubmissionType.Should().Be("SCHEDULE 13D/A");
        result.IsAmendment.Should().BeTrue();
        result.FilingType.Should().Be(FilingType.Schedule13D);
        result.DateOfEvent.Should().Be(new DateOnly(2025, 5, 2));
        result.IssuerCusip.Should().Be("000000000");
        result.ReportingPersons.Should().HaveCount(3);
    }

    [Fact]
    public void ParseFiling_Real13DAmendment_TruncatesDecimalShareAmounts()
    {
        var person = _sut.ParseFiling(
            LoadCassette(),
            "0001011438-25-000254",
            "0001362558",
            default
        ).ReportingPersons[0];

        person.Name.Should().Be("Abu Dhabi Investment Authority");
        person.Cik.Should().Be("1362558");
        // soleVotingPower is filed as 942779.94 — truncated to whole shares.
        person.SoleVotingPower.Should().Be(942779);
        person.SharedVotingPower.Should().Be(0);
        // soleDispositivePower / aggregate filed as 11615772.8.
        person.SoleDispositivePower.Should().Be(11615772);
        person.AggregateAmountOwned.Should().Be(11615772);
        person.PercentOfClass.Should().Be(60.4m);
        person.TypeOfReportingPerson.Should().Be("OO");
        person.CitizenshipOrOrganization.Should().Be("C0");
    }
}

using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay against a real Schedule 13G (Citadel Advisors' 2025-05-06 13G on
// Spirit Aviation). 13G uses different element names than 13D — a separate
// event-date element, lowercase issuer CIK/CUSIP, a different reporting-person
// container with nested voting powers, and classPercent rather than
// percentOfClass — so this proves the one parser reconciles both forms.
public class Filing13DGXmlParserParse13GTests
{
    private readonly Filing13DGXmlParser _sut = new();

    private static string LoadCassette() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings13DG",
                "sc13g-citadel-spirit.xml"
            )
        );

    [Fact]
    public void ParseFiling_Real13G_ExtractsCoverPageUsingTheGVariantElementNames()
    {
        var result = _sut.ParseFiling(
            LoadCassette(),
            accessionNumber: "0001104659-25-045128",
            cik: "0001423053",
            filingDate: new DateOnly(2025, 5, 6)
        );

        result.SubmissionType.Should().Be("SCHEDULE 13G");
        result.FilingType.Should().Be(FilingType.Schedule13G);
        result.IsAmendment.Should().BeFalse();
        result.FilerCik.Should().Be("1423053");
        // 13G files the event date under eventDateRequiresFilingThisStatement.
        result.DateOfEvent.Should().Be(new DateOnly(2025, 4, 29));
        // 13G lowercases issuerCik / issuerCusip.
        result.IssuerCik.Should().Be("1498710");
        result.IssuerCusip.Should().Be("84863V101");
        result.IssuerName.Should().Be("Spirit Aviation Holdings, Inc.");
        result.ReportingPersons.Should().HaveCount(7);
    }

    [Fact]
    public void ParseFiling_Real13G_ReadsNestedVotingPowersAndClassPercent()
    {
        var person = _sut.ParseFiling(
            LoadCassette(),
            "0001104659-25-045128",
            "0001423053",
            default
        ).ReportingPersons[0];

        person.Name.Should().Be("Citadel Advisors LLC");
        // 13G omits per-person CIK entirely.
        person.Cik.Should().BeNull();
        // Powers nest under reportingPersonBeneficiallyOwnedNumberOfShares and are
        // filed as decimals (e.g. 1624818.00) — truncated to whole shares.
        person.SoleVotingPower.Should().Be(0);
        person.SharedVotingPower.Should().Be(1624818);
        person.SoleDispositivePower.Should().Be(0);
        person.SharedDispositivePower.Should().Be(1624818);
        person.AggregateAmountOwned.Should().Be(1624818);
        // Sourced from classPercent, not percentOfClass.
        person.PercentOfClass.Should().Be(9.9m);
        person.TypeOfReportingPerson.Should().Be("IA");
        person.CitizenshipOrOrganization.Should().Be("DE");
    }
}

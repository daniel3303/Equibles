using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Adversarial: the parser documents itself as resilient — every unparseable
// field degrades to a default (ParseShares -> 0, ParsePercent -> null,
// ParseSecDate -> MinValue) so "the filing is never silently dropped". A share
// count that parses as a decimal but exceeds long.MaxValue must follow the same
// contract and degrade to 0, not throw OverflowException and crash the whole
// filing parse.
public class Filing13DGXmlParserShareOverflowTests
{
    private readonly Filing13DGXmlParser _sut = new();

    // 20 nines ~= 1e20, comfortably inside decimal's range but above
    // long.MaxValue (~9.2e18). decimal.TryParse succeeds; the (long) cast in
    // ParseShares is the only place this value goes.
    private const string OverflowingShareCount = "99999999999999999999";

    private static string MinimalFilingWithSoleVotingPower(string soleVotingPower) =>
        $"""
            <edgarSubmission>
              <headerData>
                <submissionType>SCHEDULE 13D</submissionType>
              </headerData>
              <coverPageHeader>
                <issuerInfo>
                  <issuerName>Test Issuer Inc.</issuerName>
                </issuerInfo>
                <reportingPersonInfo>
                  <reportingPersonName>Overflow Holder LP</reportingPersonName>
                  <soleVotingPower>{soleVotingPower}</soleVotingPower>
                </reportingPersonInfo>
              </coverPageHeader>
            </edgarSubmission>
            """;

    [Fact]
    public void ParseFiling_ShareCountAboveInt64Max_DegradesToZeroRatherThanThrowing()
    {
        var xml = MinimalFilingWithSoleVotingPower(OverflowingShareCount);

        var person = _sut.ParseFiling(
                xml,
                accessionNumber: "0001140361-25-000001",
                cik: "0001234567",
                filingDate: new DateOnly(2025, 1, 1)
            )
            .ReportingPersons.Single();

        person.SoleVotingPower.Should().Be(0);
    }
}

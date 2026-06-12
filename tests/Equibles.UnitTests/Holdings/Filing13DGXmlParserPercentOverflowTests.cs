using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Adversarial: the parser documents itself as resilient — every unparseable
// field degrades to a default so "the filing is never silently dropped". A
// percent-of-class that parses as a decimal but exceeds the holdings columns'
// numeric(7,4) storage cap (999.9999) must follow the same contract and
// degrade to null, not flow into the entity and abort the whole batch flush
// with a numeric-overflow error downstream.
public class Filing13DGXmlParserPercentOverflowTests
{
    private readonly Filing13DGXmlParser _sut = new();

    private static string MinimalFilingWithPercentOfClass(string percentOfClass) =>
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
                  <percentOfClass>{percentOfClass}</percentOfClass>
                </reportingPersonInfo>
              </coverPageHeader>
            </edgarSubmission>
            """;

    private decimal? ParsedPercent(string percentOfClass) =>
        _sut.ParseFiling(
                MinimalFilingWithPercentOfClass(percentOfClass),
                accessionNumber: "0001140361-25-000002",
                cik: "0001234567",
                filingDate: new DateOnly(2025, 1, 1)
            )
            .ReportingPersons.Single()
            .PercentOfClass;

    // A fat-fingered cover-page value like "5,000" parses to 5000 once the thousands
    // separator is stripped — past the storage cap, so it degrades to null.
    [Theory]
    [InlineData("5,000")]
    [InlineData("1000")]
    [InlineData("-1000")]
    public void ParseFiling_PercentBeyondTheStorageCap_DegradesToNull(string raw)
    {
        ParsedPercent(raw).Should().BeNull();
    }

    // The cap itself and ordinary form values stay faithful.
    [Theory]
    [InlineData("999.9999", 999.9999)]
    [InlineData("9.8", 9.8)]
    public void ParseFiling_PercentWithinTheStorageCap_IsKept(string raw, double expected)
    {
        ParsedPercent(raw).Should().Be((decimal)expected);
    }
}

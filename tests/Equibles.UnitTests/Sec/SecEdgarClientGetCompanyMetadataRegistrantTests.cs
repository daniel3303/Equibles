using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The registrant facts EDGAR's submissions record carries beside the classification header,
/// with recent-filing rows trimmed from real records (fetched 2026-10-22): Sixth Street
/// Specialty Lending (CIK 1508655) files its reports under 001-36364 but its 2011 N-54A
/// election sits in the recent list; Oaktree Specialty Lending (CIK 1414932) files its 10-Q
/// under its 814- number; TE Connectivity (CIK 1385157) writes its Irish address in the
/// foreign-location shape; General Motors (CIK 1467858) has a blank incorporation code.
/// </summary>
public class SecEdgarClientGetCompanyMetadataRegistrantTests
{
    private const string SixthStreet = """
        {"cik":"0001508655","entityType":"operating","sic":"","name":"Sixth Street Specialty Lending, Inc.","stateOfIncorporation":"DE",
         "addresses":{"business":{"city":"DALLAS","stateOrCountry":"TX","isForeignLocation":null,"countryCode":null}},
         "filings":{"recent":{
           "accessionNumber":["0001508655-26-000040","0001508655-26-000039","0001193125-11-100001"],
           "filingDate":["2026-08-04","2026-08-04","2011-04-15"],
           "reportDate":["2026-08-04","2026-06-30",""],
           "form":["8-K","10-Q","N-54A"],
           "primaryDocument":["a.htm","b.htm","c.htm"],
           "fileNumber":["001-36364","001-36364","814-00854"]},
          "files":[]}}
        """;

    private const string Oaktree = """
        {"cik":"0001414932","entityType":"operating","sic":"","name":"Oaktree Specialty Lending Corp","stateOfIncorporation":"DE",
         "addresses":{"business":{"city":"LOS ANGELES","stateOrCountry":"CA"}},
         "filings":{"recent":{
           "accessionNumber":["0001414932-26-000020","0001414932-26-000019","0001414932-25-000010"],
           "filingDate":["2026-09-16","2026-08-05","2025-11-18"],
           "reportDate":["2026-09-16","2026-06-30","2025-09-30"],
           "form":["8-K","10-Q","10-K"],
           "primaryDocument":["a.htm","b.htm","c.htm"],
           "fileNumber":["814-00755","814-00755","814-00755"]},
          "files":[{"name":"CIK0001414932-submissions-001.json","filingCount":1200,"filingFrom":"2007-10-01","filingTo":"2019-01-01"}]}}
        """;

    private const string TeConnectivity = """
        {"cik":"0001385157","entityType":"operating","sic":"3678","name":"TE Connectivity plc","stateOfIncorporation":"",
         "addresses":{"business":{"city":"GALWAY","stateOrCountry":null,"isForeignLocation":1,"country":"Ireland","countryCode":"L2"}},
         "filings":{"recent":{},"files":[]}}
        """;

    private const string GeneralMotors = """
        {"cik":"0001467858","entityType":"operating","sic":"3711","name":"GENERAL MOTORS CO","stateOfIncorporation":"",
         "addresses":{"business":{"city":"DETROIT","stateOrCountry":"MI"}},
         "filings":{"recent":{
           "accessionNumber":["0001467858-26-000100"],
           "filingDate":["2026-10-20"],
           "reportDate":["2026-09-30"],
           "form":["10-Q"],
           "primaryDocument":["a.htm"]},
          "files":[]}}
        """;

    [Fact]
    public async Task GetCompanyMetadata_ElectionInRecentList_ReadsTheN54AButNotItsFileNumber()
    {
        var metadata = await Client(SixthStreet).GetCompanyMetadata("1508655");

        metadata.BdcElectionFiledOn.Should().Be(new DateOnly(2011, 4, 15));
        metadata.BdcWithdrawalFiledOn.Should().BeNull();
        metadata.PeriodicReportFileNumber.Should().Be("001-36364");
        metadata.BusinessStateOrCountry.Should().Be("TX");
        metadata.HasArchivedFilings.Should().BeFalse();
    }

    [Fact]
    public async Task GetCompanyMetadata_ReportsIndexedUnderBdcNumber_ReadsTheNewestPeriodicReportNumber()
    {
        var metadata = await Client(Oaktree).GetCompanyMetadata("1414932");

        metadata.PeriodicReportFileNumber.Should().Be("814-00755");
        metadata.BdcElectionFiledOn.Should().BeNull();
        metadata.HasArchivedFilings.Should().BeTrue();
    }

    [Fact]
    public async Task GetCompanyMetadata_ForeignLocationShape_ReadsTheCountryCode()
    {
        var metadata = await Client(TeConnectivity).GetCompanyMetadata("1385157");

        metadata.BusinessStateOrCountry.Should().Be("L2");
        metadata.StateOfIncorporation.Should().BeEmpty();
        metadata.PeriodicReportFileNumber.Should().BeNull();
    }

    [Fact]
    public async Task GetCompanyMetadata_FeedWithoutFileNumbers_LeavesTheNumberUnknown()
    {
        var metadata = await Client(GeneralMotors).GetCompanyMetadata("1467858");

        metadata.BusinessStateOrCountry.Should().Be("MI");
        metadata.StateOfIncorporation.Should().BeEmpty();
        metadata.PeriodicReportFileNumber.Should().BeNull();
    }

    private static SecEdgarClient Client(string submissions)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        return new SecEdgarClient(
            new HttpClient(new SubmissionsHandler(submissions)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );
    }

    private sealed class SubmissionsHandler(string submissions) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(submissions),
                }
            );
    }
}

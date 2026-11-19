using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// SEC's ticker directory carried Anthropic (ANT) with a blank exchange on its first trading day, so
/// the company sync skipped it. The registrants that held an exchange certification at the same
/// time were mostly not fresh listings: two still amending, one withdrawn, one delisted, one not yet
/// priced. These EDGAR submissions, recorded on 2026-11-19, pin which ones the rule admits.
/// </summary>
public class SecListingEvidenceTests
{
    private static readonly DateOnly RecordedOn = new(2026, 11, 19);

    internal static async Task<List<FilingData>> RecordedFilings(string cik, DateOnly? fromDate)
    {
        var body = await File.ReadAllTextAsync(
            Path.Combine("TestAssets", "Sec", "ListingEvidence", $"CIK{cik.PadLeft(10, '0')}.json")
        );
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var client = new SecEdgarClient(
            new HttpClient(new CassetteHandler(body)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );
        return await client.GetCompanyFilings(cik, fromDate: fromDate);
    }

    private static async Task<SecListingEvidenceResult> Evaluate(
        string cik,
        bool lookback = true
    ) =>
        SecListingEvidence.Evaluate(
            await RecordedFilings(
                cik,
                lookback ? RecordedOn.AddDays(-SecListingEvidence.LookbackDays) : null
            ),
            RecordedOn
        );

    [Fact]
    public async Task Evaluate_RegistrationCertificationThenFinalProspectus_Admits()
    {
        var result = await Evaluate("2130357");

        result
            .Should()
            .Be(
                new SecListingEvidenceResult(
                    SecListingEvidenceOutcome.Admitted,
                    "0001628280-26-074342",
                    "0001354457-26-001088",
                    "0001628280-26-074944",
                    new DateOnly(2026, 11, 19)
                )
            );
    }

    [Theory]
    [InlineData("2087035")] // AfterNext Acquisition: certified 08-19, still amending its S-1.
    [InlineData("2131072")] // Harbour Island Acquisition: certified 10-13, still amending its S-1.
    [InlineData("2078148")] // Zoar: certified 11-17, not yet priced.
    public async Task Evaluate_CertifiedWithoutFinalProspectus_IsNotAdmitted(string cik)
    {
        (await Evaluate(cik)).Outcome.Should().Be(SecListingEvidenceOutcome.NoFinalProspectus);
    }

    // New Iceland Arctic Acquisition: certified and effective 09-24, withdrawn (RW) 10-05.
    [Fact]
    public async Task Evaluate_WithdrawalAfterCertification_IsNotAdmitted()
    {
        (await Evaluate("2148245")).Outcome.Should().Be(SecListingEvidenceOutcome.Withdrawn);
    }

    // BPGC Acquisition: listed in 2021, delisted by the exchange's 25-NSE in 2024.
    [Fact]
    public async Task Evaluate_DelistingAfterCertification_IsNotAdmitted()
    {
        (await Evaluate("1841610", lookback: false))
            .Outcome.Should()
            .Be(SecListingEvidenceOutcome.Withdrawn);
    }

    // Northann: certified and priced in 2023, now shown by SEC with no exchange.
    [Fact]
    public async Task Evaluate_OldListing_IsNotAdmitted()
    {
        (await Evaluate("1923780", lookback: false))
            .Outcome.Should()
            .Be(SecListingEvidenceOutcome.Stale);
        (await Evaluate("1923780")).Outcome.Should().Be(SecListingEvidenceOutcome.NoCertification);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void StillAdmitted_CountsTheWindowFromTheProspectusDate(int daysLater, bool expected)
    {
        var check = new SecListingEvidenceCheck
        {
            Outcome = SecListingEvidenceOutcome.Admitted,
            ProspectusFiledOn = RecordedOn,
        };

        SecListingEvidence
            .StillAdmitted(check, RecordedOn.AddDays(daysLater))
            .Should()
            .Be(expected);
    }

    private sealed class CassetteHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }
}

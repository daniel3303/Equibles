using System.Reflection;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class NCenFilingProcessorParseFilingDedupTests
{
    // Contract (doc-comment): a multi-series fund repeats the same firms across series, so the
    // provider list is de-duplicated by role + name. The same investment adviser listed under two
    // series must therefore collapse to ONE entry. The existing Process test only asserts provider
    // PRESENCE (.Contain), which a dedup regression would still satisfy — this pins the count.
    [Fact]
    public void ParseFiling_SameAdviserAcrossTwoSeries_DeduplicatesToOneProvider()
    {
        var processor = new NCenFilingProcessor(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<NCenFilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var series =
            "<investmentAdvisers><investmentAdviser>"
            + "<investmentAdviserName>Acme Advisers LLC</investmentAdviserName>"
            + "</investmentAdviser></investmentAdvisers>";
        var root = XElement.Parse(
            "<edgarSubmission><formData>"
                + "<registrantInfo><registrantFullName>Test Fund Trust</registrantFullName></registrantInfo>"
                + "<managementInvestmentQuestionSeriesInfo>"
                + $"<managementInvestmentQuestion>{series}</managementInvestmentQuestion>"
                + $"<managementInvestmentQuestion>{series}</managementInvestmentQuestion>"
                + "</managementInvestmentQuestionSeriesInfo>"
                + "</formData></edgarSubmission>"
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000001",
            FilingDate = new DateOnly(2025, 1, 31),
            ReportDate = new DateOnly(2024, 12, 31),
            Form = "N-CEN",
        };

        var method = typeof(NCenFilingProcessor).GetMethod(
            "ParseFiling",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var entity = (NCenFiling)method.Invoke(processor, [root, Guid.NewGuid(), filing]);

        entity
            .ServiceProviders.Count(p =>
                p.ProviderType == NCenServiceProviderType.InvestmentAdviser
            )
            .Should()
            .Be(1);
    }
}

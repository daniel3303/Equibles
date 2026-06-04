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

public class NCenFilingProcessorDedupCaseInsensitiveTests
{
    // The provider de-dup key uppercases the name (GroupBy on Name.ToUpperInvariant()),
    // so the same firm listed with different casing across series must collapse to ONE
    // entry. The existing dedup pin uses identical-case names, which a regression
    // dropping ToUpperInvariant would still satisfy; this varies only the casing.
    [Fact]
    public void ParseFiling_SameAdviserDifferingOnlyInCase_DeduplicatesToOneProvider()
    {
        var processor = new NCenFilingProcessor(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<NCenFilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        string Series(string adviserName) =>
            "<investmentAdvisers><investmentAdviser>"
            + $"<investmentAdviserName>{adviserName}</investmentAdviserName>"
            + "</investmentAdviser></investmentAdvisers>";

        var root = XElement.Parse(
            "<edgarSubmission><formData>"
                + "<registrantInfo><registrantFullName>Test Fund Trust</registrantFullName></registrantInfo>"
                + "<managementInvestmentQuestionSeriesInfo>"
                + $"<managementInvestmentQuestion>{Series("Acme Advisers LLC")}</managementInvestmentQuestion>"
                + $"<managementInvestmentQuestion>{Series("ACME ADVISERS LLC")}</managementInvestmentQuestion>"
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

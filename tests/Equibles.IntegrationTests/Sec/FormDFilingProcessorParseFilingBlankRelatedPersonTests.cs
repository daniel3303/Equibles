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

public class FormDFilingProcessorParseFilingBlankRelatedPersonTests
{
    // A Form D relatedPersonInfo with neither a name nor any relationship is a placeholder, not a
    // real related person, so ParseRelatedPerson drops it (return null). The existing Process test
    // only exercises populated related persons; this pins the skip. A regression dropping the
    // name+relationship empty guard would add a blank related person. Oracle from the contract.
    [Fact]
    public void ParseFiling_RelatedPersonWithNoNameAndNoRelationship_IsSkippedNotAddedBlank()
    {
        var processor = new FormDFilingProcessor(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<FormDFilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var root = XElement.Parse(
            "<edgarSubmission>"
                + "<offeringData><offeringSalesAmounts>"
                + "<totalOfferingAmount>1000</totalOfferingAmount>"
                + "</offeringSalesAmounts></offeringData>"
                + "<relatedPersonsList><relatedPersonInfo></relatedPersonInfo></relatedPersonsList>"
                + "</edgarSubmission>"
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000001",
            FilingDate = new DateOnly(2025, 1, 31),
            ReportDate = new DateOnly(2024, 12, 31),
            Form = "D",
        };

        var method = typeof(FormDFilingProcessor).GetMethod(
            "ParseFiling",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var entity = (FormDFiling)method.Invoke(processor, [root, Guid.NewGuid(), filing]);

        entity.RelatedPersons.Should().BeEmpty();
    }
}

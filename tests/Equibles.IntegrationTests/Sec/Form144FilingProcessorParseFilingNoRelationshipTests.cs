using System.Reflection;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class Form144FilingProcessorParseFilingNoRelationshipTests
{
    // RelationshipToIssuer joins the relationshipToIssuer entries; when a Form 144 lists none, the
    // field must be null ("not reported"), not an empty string. The existing tests pin the single
    // and comma-joined cases; this pins the empty-join coalesce. A regression dropping it would
    // store "" and blur "no relationship reported" from "reported blank". Oracle from the contract.
    [Fact]
    public void ParseFiling_NoRelationshipToIssuerEntries_RelationshipIsNull()
    {
        var processor = new Form144FilingProcessor(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Form144FilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var root = XElement.Parse(
            "<edgarSubmission><formData>"
                + "<issuerInfo><nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>Jane Doe"
                + "</nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold></issuerInfo>"
                + "<securitiesInformation><securitiesClassTitle>COM</securitiesClassTitle></securitiesInformation>"
                + "</formData></edgarSubmission>"
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000001",
            FilingDate = new DateOnly(2025, 1, 31),
            ReportDate = new DateOnly(2024, 12, 31),
            Form = "144",
        };

        var method = typeof(Form144FilingProcessor).GetMethod(
            "ParseFiling",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var entity = (Form144Filing)method.Invoke(processor, [root, Guid.NewGuid(), filing]);

        entity.RelationshipToIssuer.Should().BeNull();
    }
}

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

public class FormDFilingProcessorParseFilingIndefiniteAmountTests
{
    // Form D offerings may report "Indefinite" instead of a dollar figure. ParseAmount maps that
    // to (null, flagged) — but the existing tests only exercise ParseAmount in isolation and the
    // numeric path through Process. This pins the wiring: an Indefinite totalOfferingAmount sets
    // IsOfferingAmountIndefinite=true AND leaves TotalOfferingAmount null. Oracle from the contract.
    [Fact]
    public void ParseFiling_IndefiniteTotalOfferingAmount_FlagsIndefiniteAndLeavesAmountNull()
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
            "<edgarSubmission><offeringData>"
                + "<offeringSalesAmounts>"
                + "<totalOfferingAmount>Indefinite</totalOfferingAmount>"
                + "</offeringSalesAmounts>"
                + "</offeringData></edgarSubmission>"
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

        entity.IsOfferingAmountIndefinite.Should().BeTrue();
        entity.TotalOfferingAmount.Should().BeNull();
    }
}

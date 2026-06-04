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

public class NportFilingProcessorParseFilingBlankHoldingTests
{
    // An NPORT invstOrSec with neither a usable name nor a USD value is a placeholder, not a real
    // portfolio holding, so ParseHolding drops it (return null). The Process test only exercises
    // populated holdings; this pins the skip. A regression dropping the guard would add a blank
    // NportHolding; flipping && to || would over-drop real holdings. Oracle from the contract.
    [Fact]
    public void ParseFiling_InvestmentWithNoNameAndNoUsdValue_IsSkippedNotAddedAsBlankHolding()
    {
        var processor = new NportFilingProcessor(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<NportFilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var root = XElement.Parse(
            "<edgarSubmission><formData>"
                + "<genInfo><regName>Test Fund</regName></genInfo>"
                + "<fundInfo><invstOrSecs><invstOrSec></invstOrSec></invstOrSecs></fundInfo>"
                + "</formData></edgarSubmission>"
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000001",
            FilingDate = new DateOnly(2025, 1, 31),
            ReportDate = new DateOnly(2024, 12, 31),
            Form = "NPORT-P",
        };

        var method = typeof(NportFilingProcessor).GetMethod(
            "ParseFiling",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var entity = (NportFiling)method.Invoke(processor, [root, Guid.NewGuid(), filing]);

        entity.Holdings.Should().BeEmpty();
    }
}

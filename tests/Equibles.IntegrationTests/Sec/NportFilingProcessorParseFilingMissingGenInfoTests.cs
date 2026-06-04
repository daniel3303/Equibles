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

public class NportFilingProcessorParseFilingMissingGenInfoTests
{
    // genInfo is NPORT's required section. A submission that parses as valid XML but lacks it
    // can't form a filing, so ParseFiling returns null (the base then logs and skips). The existing
    // tests cover empty and non-XML content; this pins the well-formed-but-missing-section branch,
    // a distinct guard from those. Oracle from the documented RequiredSection contract.
    [Fact]
    public void ParseFiling_WellFormedXmlMissingGenInfo_ReturnsNull()
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
            "<edgarSubmission><formData><fundInfo><totAssets>100</totAssets></fundInfo></formData></edgarSubmission>"
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

        entity.Should().BeNull();
    }
}

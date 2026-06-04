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

public class Form144FilingProcessorParseFilingMissingFormDataTests
{
    // formData is Form 144's required section. A submission that parses as valid XML but lacks it
    // can't form a filing, so ParseFiling returns null (the base then logs and skips). The existing
    // tests cover empty and non-XML content; this pins the well-formed-but-missing-section branch,
    // a distinct guard from those. Oracle from the documented RequiredSection contract.
    [Fact]
    public void ParseFiling_WellFormedXmlMissingFormData_ReturnsNull()
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
            "<edgarSubmission><headerData><submissionType>144</submissionType></headerData></edgarSubmission>"
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

        entity.Should().BeNull();
    }
}

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

public class NportFilingProcessorParseIssuerCategoryConditionalTests
{
    // Contract (ParseIssuerCategory doc): NPORT reports the issuer category either as
    // an <issuerCat> element OR, for conditional categories (e.g. swaps), as the
    // issuerCat attribute on <issuerConditional>. When the direct element is absent,
    // the attribute fallback must supply the value. The Process test only exercises
    // the direct-element path; this pins the conditional-attribute fallback branch.
    [Fact]
    public void ParseFiling_IssuerCategoryOnlyOnConditionalAttribute_UsesAttributeFallback()
    {
        var processor = new NportFilingProcessor(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<NportFilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        // invstOrSecs is a child of formData (a sibling of fundInfo), as in real EDGAR filings.
        var root = XElement.Parse(
            "<edgarSubmission><formData>"
                + "<genInfo><regName>Test Fund</regName></genInfo>"
                + "<invstOrSecs><invstOrSec>"
                + "<name>Acme Total Return Swap</name>"
                + "<issuerConditional issuerCat=\"CORP\" />"
                + "</invstOrSec></invstOrSecs>"
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

        entity.Holdings.Should().ContainSingle();
        entity.Holdings[0].IssuerCategory.Should().Be("CORP");
    }
}

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

public class Form144FilingProcessorParsePriorSaleEmptyShellTests
{
    // Contract (ParsePriorSale doc): "When the filer reports nothing to disclose, the
    // element can still be emitted as an empty shell — skip rows that carry no sale
    // data so they don't pollute the table." An empty securitiesSoldInPast3Months
    // element (no saleDate, no amount, no grossProceeds) must produce NO prior-sale
    // row. The happy-path pin only adds a populated sale; this pins the skip guard.
    [Fact]
    public void ParseFiling_PriorSaleElementWithNoSaleData_IsSkipped()
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
                + "<securitiesSoldInPast3Months><sellerDetails><name>Jane Doe</name></sellerDetails>"
                + "</securitiesSoldInPast3Months>"
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

        entity.PriorSales.Should().BeEmpty();
    }
}

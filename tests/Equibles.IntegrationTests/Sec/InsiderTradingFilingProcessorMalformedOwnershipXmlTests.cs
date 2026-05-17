using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// `Process` has two distinct catches: an XmlException catch that skips
/// malformed-but-&lt;ownershipDocument&gt; filings QUIETLY (no ErrorReporter),
/// and a generic catch that DOES report. The existing MalformedXml pin feeds
/// `&lt;not&gt;valid&lt;xml` — that lacks &lt;ownershipDocument&gt; so it returns at the
/// legacy-skip guard and never reaches XDocument.Parse. The XmlException
/// catch's contract — return false AND do not flood the Errors table (these
/// broken historical filings are "expected, non-actionable, numerous") — is
/// unexercised. A regression routing it to the generic catch would persist a
/// junk Error row per malformed filing.
/// </summary>
public class InsiderTradingFilingProcessorMalformedOwnershipXmlTests
{
    [Fact]
    public async Task Process_MalformedXmlContainingOwnershipDocument_ReturnsFalseAndReportsNoError()
    {
        var dbContext = TestDbContextFactory.Create(
            new InsiderTradingModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var errorRepo = new ErrorRepository(dbContext);
        var secClient = Substitute.For<ISecEdgarClient>();
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(InsiderOwnerRepository), new InsiderOwnerRepository(dbContext)),
            (typeof(InsiderTransactionRepository), new InsiderTransactionRepository(dbContext)),
            (typeof(ErrorManager), new ErrorManager(new ErrorRepository(dbContext)))
        );
        var processor = new InsiderTradingFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );

        // Contains <ownershipDocument> (passes the legacy-skip guard) but the
        // <reportingOwner> tag is closed by </ownershipDocument> — mismatched,
        // so XDocument.Parse throws XmlException, hitting the QUIET catch.
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns("<ownershipDocument><reportingOwner></ownershipDocument>");

        var filing = new FilingData
        {
            AccessionNumber = $"0001-24-{Guid.NewGuid().ToString("N")[..6]}",
            Form = "4",
            FilingDate = new DateOnly(2024, 3, 16),
            ReportDate = new DateOnly(2024, 3, 15),
            Cik = "0000320193",
        };
        var company = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };

        var result = await processor.Process(filing, company);

        result.Should().BeFalse();
        errorRepo
            .GetAll()
            .ToList()
            .Should()
            .BeEmpty("malformed ownership XML is an expected quiet skip, not an Errors-table row");
    }
}

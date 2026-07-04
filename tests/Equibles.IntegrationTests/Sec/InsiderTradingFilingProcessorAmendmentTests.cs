using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

// Form 4/A / 3/A supersession: an amendment restates its original filing in full,
// so the pipeline must (1) replace the original's transactions when the amendment
// ingests, (2) skip a late-arriving original whose amendment already ingested
// (EDGAR lists newest-first, so that order is the COMMON one during history
// sweeps), and (3) skip an older amendment when a newer one of the same original
// is already in. Without these, enabling 4/A sync would double count insider
// trades — the erroneous original and its correction summed together.
public class InsiderTradingFilingProcessorAmendmentTests
{
    private const string OriginalAccession = "0001-24-000100";
    private const string AmendmentAccession = "0001-24-000200";
    private static readonly DateOnly OriginalFilingDate = new(2024, 3, 16);
    private static readonly DateOnly AmendmentFilingDate = new(2024, 4, 2);

    private static readonly string OriginalForm4Xml = BuildOwnershipXml(
        shares: 1000,
        dateOfOriginalSubmission: null
    );

    // The amendment corrects the share count and names its original via
    // dateOfOriginalSubmission — the document-level element EDGAR requires on /A
    // ownership filings.
    private static readonly string AmendmentForm4Xml = BuildOwnershipXml(
        shares: 250,
        dateOfOriginalSubmission: OriginalFilingDate
    );

    private static string BuildOwnershipXml(long shares, DateOnly? dateOfOriginalSubmission)
    {
        var originalSubmission = dateOfOriginalSubmission.HasValue
            ? $"<dateOfOriginalSubmission>{dateOfOriginalSubmission:yyyy-MM-dd}</dateOfOriginalSubmission>"
            : string.Empty;
        return $"""
            <ownershipDocument>
                {originalSubmission}
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001234567</rptOwnerCik>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                    <reportingOwnerRelationship>
                        <isDirector>1</isDirector>
                    </reportingOwnerRelationship>
                </reportingOwner>
                <nonDerivativeTable>
                    <nonDerivativeTransaction>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <transactionDate><value>2024-03-15</value></transactionDate>
                        <transactionCoding><transactionCode>P</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>{shares}</value></transactionShares>
                            <transactionPricePerShare><value>150.50</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>5000</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeTransaction>
                </nonDerivativeTable>
            </ownershipDocument>
            """;
    }

    private static (
        InsiderTradingFilingProcessor Processor,
        InsiderTransactionRepository TxRepo,
        ISecEdgarClient SecClient
    ) CreateProcessorWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new InsiderTradingModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new YahooModuleConfiguration()
        );

        var ownerRepo = new InsiderOwnerRepository(dbContext);
        var txRepo = new InsiderTransactionRepository(dbContext);
        var filingRepo = new InsiderFilingRepository(dbContext);
        var errorManager = new ErrorManager(new ErrorRepository(dbContext));
        var dailyStockPriceRepo = new DailyStockPriceRepository(dbContext);
        var secClient = Substitute.For<ISecEdgarClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(InsiderOwnerRepository), ownerRepo),
            (typeof(InsiderTransactionRepository), txRepo),
            (typeof(InsiderFilingRepository), filingRepo),
            (typeof(IFileManager), Substitute.For<IFileManager>()),
            (typeof(ErrorManager), errorManager),
            (typeof(DailyStockPriceRepository), dailyStockPriceRepo),
            (typeof(InsiderTransactionPriceValidator), new InsiderTransactionPriceValidator())
        );

        var processor = new InsiderTradingFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );

        return (processor, txRepo, secClient);
    }

    private static FilingData MakeOriginal() =>
        new()
        {
            AccessionNumber = OriginalAccession,
            Form = "4",
            FilingDate = OriginalFilingDate,
            ReportDate = new DateOnly(2024, 3, 15),
            Cik = "0000320193",
        };

    private static FilingData MakeAmendment() =>
        new()
        {
            AccessionNumber = AmendmentAccession,
            Form = "4/A",
            FilingDate = AmendmentFilingDate,
            ReportDate = new DateOnly(2024, 3, 15),
            Cik = "0000320193",
        };

    private static CommonStock MakeCompany() =>
        new()
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };

    [Fact]
    public async Task Process_AmendmentAfterOriginal_ReplacesTheOriginalsTransactions()
    {
        var (processor, txRepo, secClient) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        var result = await processor.Process(MakeAmendment(), company);

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().ToList();
        transactions
            .Should()
            .ContainSingle("the amendment replaces, never sums with, its original");
        transactions[0].AccessionNumber.Should().Be(AmendmentAccession);
        transactions[0].Shares.Should().Be(250);
        transactions[0].IsAmendment.Should().BeTrue();
        transactions[0].OriginalFilingDate.Should().Be(OriginalFilingDate);
        transactions[0]
            .SupersededAccessionNumber.Should()
            .Be(OriginalAccession, "the amendment records which original it replaced");
    }

    [Fact]
    public async Task Process_OriginalAfterItsAmendment_IsSkipped()
    {
        // EDGAR's submissions feed lists newest-first, so during a history sweep
        // the 4/A routinely processes BEFORE its Form 4.
        var (processor, txRepo, secClient) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        var result = await processor.Process(MakeOriginal(), company);

        result.Should().BeFalse("the original's rows were already superseded");
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().ContainSingle();
        transactions[0].AccessionNumber.Should().Be(AmendmentAccession);
        transactions[0].Shares.Should().Be(250);
        transactions[0]
            .SupersededAccessionNumber.Should()
            .Be(
                OriginalAccession,
                "the orphaned amendment claims the original so future sweeps drop it without a fetch"
            );
    }

    [Fact]
    public async Task Process_AmendmentWithDateShiftedOriginal_StillSupersedesIt()
    {
        // EDGAR indexes an after-17:30 submission the NEXT business day, so the
        // original's feed FilingDate can trail the amendment's filer-entered
        // dateOfOriginalSubmission. The window resolution must still find and
        // replace it — exact-date-only matching would leave both rows counted.
        var (processor, txRepo, secClient) = CreateProcessorWithDeps();
        var company = MakeCompany();

        var shiftedOriginal = MakeOriginal();
        shiftedOriginal.FilingDate = OriginalFilingDate.AddDays(3); // Friday 18:00 → Monday index
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(shiftedOriginal, company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        var result = await processor.Process(MakeAmendment(), company);

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().ContainSingle("the date-shifted original must still be replaced");
        transactions[0].AccessionNumber.Should().Be(AmendmentAccession);
        transactions[0].SupersededAccessionNumber.Should().Be(OriginalAccession);
    }

    [Fact]
    public async Task FilterKnownAccessions_SupersededOriginal_CountsAsKnown()
    {
        // A superseded original has no rows of its own; without the claim column
        // every sweep would pass it through the prefilter and re-fetch it from
        // EDGAR forever just to re-skip it.
        var (processor, _, secClient) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var known = await processor.FilterKnownAccessions([
            OriginalAccession,
            AmendmentAccession,
            "0001-24-999999",
        ]);

        known.Should().BeEquivalentTo([OriginalAccession, AmendmentAccession]);
    }

    [Fact]
    public async Task Process_OlderAmendmentAfterNewer_IsSkipped()
    {
        var (processor, txRepo, secClient) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var olderAmendment = MakeAmendment();
        olderAmendment.AccessionNumber = "0001-24-000150";
        olderAmendment.FilingDate = new DateOnly(2024, 3, 20);
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(shares: 999, dateOfOriginalSubmission: OriginalFilingDate));
        var result = await processor.Process(olderAmendment, company);

        result.Should().BeFalse("a newer amendment of the same original is already ingested");
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().ContainSingle();
        transactions[0].Shares.Should().Be(250);
    }
}

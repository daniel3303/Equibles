using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using MediaFile = Equibles.Media.Data.Models.File;
using MediaFileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.Sec;

// Form 3/A, 4/A, and 5/A supersession is section-scoped: transaction rows replace
// transactions and holding snapshots replace holdings. The pipeline also handles
// amendments arriving before originals, newer amendment chains, and Form 3/4/5
// family boundaries without double-counting or deleting a sibling filing.
public class InsiderTradingFilingProcessorAmendmentTests
{
    private const string OriginalAccession = "0001-24-000100";
    private const string AmendmentAccession = "0001-24-000200";
    private const string FormFiveOriginalAccession = "0001-24-000300";
    private const string FormFiveAmendmentAccession = "0001-24-000400";
    private static readonly DateOnly OriginalFilingDate = new(2024, 3, 16);
    private static readonly DateOnly AmendmentFilingDate = new(2024, 4, 2);

    private static readonly string OriginalForm4Xml = BuildOwnershipXml(
        shares: 1000,
        dateOfOriginalSubmission: null,
        form: "4"
    );

    // The amendment corrects the share count and names its original via
    // dateOfOriginalSubmission — the document-level element EDGAR requires on /A
    // ownership filings.
    private static readonly string AmendmentForm4Xml = BuildOwnershipXml(
        shares: 250,
        dateOfOriginalSubmission: OriginalFilingDate,
        form: "4/A"
    );

    private static readonly string OriginalForm4WithHoldingXml = AddHolding(
        OriginalForm4Xml,
        shares: 5000
    );

    private static readonly string HoldingsOnlyAmendmentForm4Xml = BuildHoldingOnlyOwnershipXml(
        shares: 4500,
        dateOfOriginalSubmission: OriginalFilingDate
    );

    private static readonly string EmptyAmendmentForm4Xml = BuildEmptyAmendmentOwnershipXml(
        OriginalFilingDate
    );

    private static string BuildOwnershipXml(
        long shares,
        DateOnly? dateOfOriginalSubmission,
        string form = "4/A"
    )
    {
        var originalSubmission = dateOfOriginalSubmission.HasValue
            ? $"<dateOfOriginalSubmission>{dateOfOriginalSubmission:yyyy-MM-dd}</dateOfOriginalSubmission>"
            : string.Empty;
        return $"""
            <ownershipDocument>
                <documentType>{form}</documentType>
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

    private static string AddHolding(string xml, long shares)
    {
        var holding = $"""
                    <nonDerivativeHolding>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>{shares}</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeHolding>
            """;
        return xml.Replace("</nonDerivativeTable>", $"{holding}</nonDerivativeTable>");
    }

    private static string BuildHoldingOnlyOwnershipXml(
        long shares,
        DateOnly dateOfOriginalSubmission
    )
    {
        return $"""
            <ownershipDocument>
                <documentType>4/A</documentType>
                <dateOfOriginalSubmission>{dateOfOriginalSubmission:yyyy-MM-dd}</dateOfOriginalSubmission>
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
                    <nonDerivativeHolding>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>{shares}</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeHolding>
                </nonDerivativeTable>
            </ownershipDocument>
            """;
    }

    private static string BuildEmptyAmendmentOwnershipXml(DateOnly dateOfOriginalSubmission)
    {
        return $"""
            <ownershipDocument>
                <documentType>4/A</documentType>
                <dateOfOriginalSubmission>{dateOfOriginalSubmission:yyyy-MM-dd}</dateOfOriginalSubmission>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001234567</rptOwnerCik>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
                <nonDerivativeTable/>
                <derivativeTable/>
            </ownershipDocument>
            """;
    }

    private static string BuildNoSecuritiesOwnedAmendmentXml(DateOnly originalFilingDate)
    {
        return $"""
            <ownershipDocument>
                <documentType>3/A</documentType>
                <dateOfOriginalSubmission>{originalFilingDate:yyyy-MM-dd}</dateOfOriginalSubmission>
                <noSecuritiesOwned>1</noSecuritiesOwned>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001234567</rptOwnerCik>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
                <nonDerivativeTable/>
                <derivativeTable/>
            </ownershipDocument>
            """;
    }

    private static string BuildFormThreeHoldingXml(long shares)
    {
        return $"""
            <ownershipDocument>
                <documentType>3</documentType>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001234567</rptOwnerCik>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
                <nonDerivativeTable>
                    <nonDerivativeHolding>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>{shares}</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                    </nonDerivativeHolding>
                </nonDerivativeTable>
            </ownershipDocument>
            """;
    }

    private static (
        InsiderTradingFilingProcessor Processor,
        InsiderTransactionRepository TxRepo,
        ISecEdgarClient SecClient,
        InsiderFilingRepository FilingRepo
    ) CreateProcessorWithDeps(ILogger<InsiderTradingFilingProcessor> logger = null) =>
        CreateProcessorWithDeps(out _, out _, logger);

    private static (
        InsiderTradingFilingProcessor Processor,
        InsiderTransactionRepository TxRepo,
        ISecEdgarClient SecClient,
        InsiderFilingRepository FilingRepo
    ) CreateProcessorWithDeps(
        out IFileManager fileManager,
        out Action restoreFileManager,
        ILogger<InsiderTradingFilingProcessor> logger = null
    )
    {
        var dbContext = TestDbContextFactory.Create(
            new InsiderTradingModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new CorporateActionsModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new YahooModuleConfiguration()
        );

        var ownerRepo = new InsiderOwnerRepository(dbContext);
        var txRepo = new InsiderTransactionRepository(dbContext);
        var filingRepo = new InsiderFilingRepository(dbContext);
        var errorManager = new ErrorManager(new ErrorRepository(dbContext));
        var dailyStockPriceRepo = new DailyStockPriceRepository(dbContext);
        var secClient = Substitute.For<ISecEdgarClient>();
        var configuredFileManager = Substitute.For<IFileManager>();

        void ConfigureFileManager()
        {
            configuredFileManager
                .SaveInternalFile(
                    Arg.Any<byte[]>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>()
                )
                .Returns(call =>
                {
                    var bytes = call.ArgAt<byte[]>(0);
                    var file = new MediaFile
                    {
                        Name = call.ArgAt<string>(1),
                        Extension = call.ArgAt<string>(2),
                        ContentType = call.ArgAt<string>(3),
                        Size = bytes.Length,
                        FileContent = new MediaFileContent { Bytes = bytes },
                    };
                    dbContext.Add(file);
                    return file;
                });
            configuredFileManager
                .GetContent(Arg.Any<MediaFile>())
                .Returns(call => call.ArgAt<MediaFile>(0).FileContent.Bytes);
        }

        ConfigureFileManager();
        fileManager = configuredFileManager;
        restoreFileManager = ConfigureFileManager;

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(InsiderOwnerRepository), ownerRepo),
            (typeof(InsiderTransactionRepository), txRepo),
            (typeof(InsiderFilingRepository), filingRepo),
            (typeof(FailedFilingIngestRepository), new FailedFilingIngestRepository(dbContext)),
            (typeof(IFileManager), configuredFileManager),
            (typeof(ErrorManager), errorManager),
            (typeof(DailyStockPriceRepository), dailyStockPriceRepo),
            (typeof(InsiderTransactionPriceValidator), new InsiderTransactionPriceValidator()),
            (typeof(StockSplitRepository), new StockSplitRepository(dbContext))
        );

        var processor = new InsiderTradingFilingProcessor(
            scopeFactory,
            logger ?? Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>())
        );

        return (processor, txRepo, secClient, filingRepo);
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

    private static FilingData MakeFormFiveOriginal() =>
        new()
        {
            AccessionNumber = FormFiveOriginalAccession,
            Form = "5",
            FilingDate = OriginalFilingDate,
            ReportDate = new DateOnly(2024, 3, 15),
            Cik = "0000320193",
        };

    private static FilingData MakeFormFiveAmendment() =>
        new()
        {
            AccessionNumber = FormFiveAmendmentAccession,
            Form = "5/A",
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

    private static List<InsiderTransaction> CurrentRows(InsiderTransactionRepository repository) =>
        repository.GetAll().Where(t => t.TransactionCode != TransactionCode.IngestMarker).ToList();

    private static void AssertCurrentMarker(
        InsiderTransactionRepository repository,
        string accessionNumber
    )
    {
        repository
            .GetByAccessionNumber(accessionNumber)
            .Should()
            .ContainSingle(t =>
                t.TransactionCode == TransactionCode.IngestMarker
                && t.ParserVersion == InsiderTransaction.CurrentParserVersion
            );
    }

    [Fact]
    public async Task Process_AmendmentAfterOriginal_ReplacesTheOriginalsTransactions()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        var result = await processor.Process(MakeAmendment(), company);

        result.Should().BeTrue();
        var transactions = CurrentRows(txRepo);
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
        AssertCurrentMarker(txRepo, OriginalAccession);
    }

    [Fact]
    public async Task Process_HoldingsOnlyAmendmentAfterOriginal_PreservesOriginalTransactions()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var rows = txRepo.GetAll().OrderBy(t => t.AccessionNumber).ToList();
        rows.Should().HaveCount(2);
        rows.Single(t => t.TransactionCode != TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(OriginalAccession);
        var holding = rows.Single(t => t.TransactionCode == TransactionCode.Holding);
        holding.AccessionNumber.Should().Be(AmendmentAccession);
        holding.Shares.Should().Be(4500);
        holding.SupersededAccessionNumber.Should().Be(OriginalAccession);
    }

    [Fact]
    public async Task Process_LegacyWholesaleDeletion_ReplaysOriginalAndRestoresUntouchedSection()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        // Recreate the pre-fix production state: the holdings-only amendment
        // claimed the original after wholesale deletion removed its transaction.
        txRepo.Delete(txRepo.GetByAccessionNumber(OriginalAccession));
        await txRepo.SaveChanges();

        var knownBeforeReplay = await processor.FilterKnownAccessions([
            OriginalAccession,
            AmendmentAccession,
        ]);
        knownBeforeReplay.Should().Equal(AmendmentAccession);

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var rows = txRepo
            .GetAll()
            .Where(t => t.TransactionCode != TransactionCode.IngestMarker)
            .ToList();
        rows.Should().HaveCount(2);
        rows.Should()
            .ContainSingle(t =>
                t.AccessionNumber == OriginalAccession
                && t.TransactionCode != TransactionCode.Holding
            );
        rows.Should()
            .ContainSingle(t =>
                t.AccessionNumber == AmendmentAccession
                && t.TransactionCode == TransactionCode.Holding
            );
    }

    [Fact]
    public async Task Process_LegacyClaimAndUnresolvedAmendment_ReplayAppliesBothSections()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var transactionAmendment = MakeAmendment();
        transactionAmendment.AccessionNumber = "0001-24-000300";
        transactionAmendment.FilingDate = AmendmentFilingDate.AddDays(1);
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(transactionAmendment, company)).Should().BeTrue();

        // Recreate a mixed legacy state: wholesale deletion attached the holding
        // claim, while the disjoint transaction correction stayed unresolved.
        var holdingAmendment = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        holdingAmendment.SupersededAccessionNumber = OriginalAccession;
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company))
            .Should()
            .BeFalse("both amended sections leave the original with no current rows");

        var rows = CurrentRows(txRepo);
        rows.Should().HaveCount(2);
        rows.Should()
            .ContainSingle(t =>
                t.AccessionNumber == AmendmentAccession
                && t.TransactionCode == TransactionCode.Holding
            );
        rows.Should()
            .ContainSingle(t =>
                t.AccessionNumber == transactionAmendment.AccessionNumber
                && t.TransactionCode != TransactionCode.Holding
            );
        rows.Should().OnlyContain(t => t.SupersededAccessionNumber == OriginalAccession);
        AssertCurrentMarker(txRepo, OriginalAccession);
    }

    [Fact]
    public async Task Process_OriginalAfterHoldingsOnlyAmendment_PreservesOriginalTransactions()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var rows = txRepo.GetAll().OrderBy(t => t.AccessionNumber).ToList();
        rows.Should().HaveCount(2);
        rows.Single(t => t.TransactionCode != TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(OriginalAccession);
        rows.Single(t => t.TransactionCode == TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(AmendmentAccession);
    }

    [Fact]
    public async Task Process_OriginalAfterEmptyAmendment_PreservesEveryOriginalSection()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(EmptyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        txRepo
            .GetAll()
            .Should()
            .ContainSingle(t =>
                t.TransactionCode == TransactionCode.IngestMarker
                && t.SupersededAccessionNumber == null
            );
        (await processor.FilterKnownAccessions([AmendmentAccession]))
            .Should()
            .Equal(AmendmentAccession);

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var rows = txRepo
            .GetAll()
            .Where(t => t.TransactionCode != TransactionCode.IngestMarker)
            .ToList();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(t => t.AccessionNumber == OriginalAccession);
        rows.Should().ContainSingle(t => t.TransactionCode == TransactionCode.Holding);
        rows.Should().ContainSingle(t => t.TransactionCode != TransactionCode.Holding);
    }

    [Fact]
    public async Task Process_EmptyNewerAmendment_DoesNotSuppressOlderAmendmentSections()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var emptyNewerAmendment = MakeAmendment();
        emptyNewerAmendment.AccessionNumber = "0001-24-000300";
        emptyNewerAmendment.FilingDate = AmendmentFilingDate.AddDays(1);
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(EmptyAmendmentForm4Xml);
        (await processor.Process(emptyNewerAmendment, company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var rows = txRepo
            .GetAll()
            .Where(t => t.TransactionCode != TransactionCode.IngestMarker)
            .ToList();
        rows.Should().HaveCount(2);
        rows.Single(t => t.TransactionCode == TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(AmendmentAccession);
        rows.Single(t => t.TransactionCode != TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(OriginalAccession);
    }

    [Fact]
    public async Task Process_FormThreeOriginalAfterNoSecuritiesOwnedAmendment_IsSuperseded()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        var amendment = MakeAmendment();
        amendment.Form = "3/A";
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildNoSecuritiesOwnedAmendmentXml(OriginalFilingDate));
        (await processor.Process(amendment, company)).Should().BeTrue();

        var original = MakeOriginal();
        original.Form = "3";
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(BuildFormThreeHoldingXml(5000));
        (await processor.Process(original, company)).Should().BeFalse();

        var sentinel = CurrentRows(txRepo).Should().ContainSingle().Subject;
        sentinel.AccessionNumber.Should().Be(AmendmentAccession);
        sentinel.TransactionCode.Should().Be(TransactionCode.Holding);
        sentinel.IsAmendment.Should().BeTrue();
        sentinel.OriginalFilingDate.Should().Be(OriginalFilingDate);
        sentinel.SupersededAccessionNumber.Should().Be(OriginalAccession);
        AssertCurrentMarker(txRepo, OriginalAccession);
    }

    [Fact]
    public async Task Process_TransactionAmendment_PreservesAmbiguousPreV5Holding()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var legacyHolding = txRepo
            .GetByAccessionNumber(OriginalAccession)
            .Single(t => t.TransactionCode == TransactionCode.Holding);
        legacyHolding.TransactionCode = TransactionCode.Other;
        legacyHolding.ParserVersion = 4;
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var rows = txRepo.GetAll().ToList();
        rows.Should().HaveCount(2);
        rows.Should().Contain(legacyHolding);
        rows.Should().ContainSingle(t => t.AccessionNumber == AmendmentAccession);
    }

    [Fact]
    public async Task Process_HoldingsAmendment_PreservesAmbiguousPreV5Holding()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var legacyHolding = txRepo
            .GetByAccessionNumber(OriginalAccession)
            .Single(t => t.TransactionCode == TransactionCode.Holding);
        legacyHolding.TransactionCode = TransactionCode.Other;
        legacyHolding.ParserVersion = 4;
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var rows = txRepo.GetAll().ToList();
        rows.Should().HaveCount(3);
        rows.Should().Contain(legacyHolding);
        rows.Should()
            .ContainSingle(t =>
                t.AccessionNumber == AmendmentAccession
                && t.TransactionCode == TransactionCode.Holding
            );
    }

    [Fact]
    public async Task Process_NewerHoldingsAmendment_PreservesOlderAmendmentTransactions()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var newerAmendment = MakeAmendment();
        newerAmendment.AccessionNumber = "0001-24-000300";
        newerAmendment.FilingDate = AmendmentFilingDate.AddDays(1);
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(newerAmendment, company)).Should().BeTrue();

        var rows = CurrentRows(txRepo).OrderBy(t => t.AccessionNumber).ToList();
        rows.Should().HaveCount(2);
        rows.Single(t => t.TransactionCode != TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(AmendmentAccession);
        rows.Single(t => t.TransactionCode == TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(newerAmendment.AccessionNumber);
    }

    [Fact]
    public async Task Process_OlderTransactionAmendmentAfterNewerHoldingsAmendment_PreservesBoth()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        var newerAmendment = MakeAmendment();
        newerAmendment.AccessionNumber = "0001-24-000300";
        newerAmendment.FilingDate = AmendmentFilingDate.AddDays(1);
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(newerAmendment, company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);
        (await processor.Process(MakeOriginal(), company)).Should().BeFalse();

        var rows = CurrentRows(txRepo).OrderBy(t => t.AccessionNumber).ToList();
        rows.Should().HaveCount(2);
        rows.Single(t => t.TransactionCode != TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(AmendmentAccession);
        rows.Single(t => t.TransactionCode == TransactionCode.Holding)
            .AccessionNumber.Should()
            .Be(newerAmendment.AccessionNumber);
        rows.Should().OnlyContain(t => t.SupersededAccessionNumber == OriginalAccession);
    }

    [Fact]
    public async Task Process_PartialLateOriginalFailure_DoesNotPersistClaimAndCanRetry()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps(
            out var fileManager,
            out var restoreFileManager
        );
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(HoldingsOnlyAmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        fileManager
            .SaveInternalFile(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(Task.FromException<MediaFile>(new IOException("simulated capture failure")));
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4WithHoldingXml);

        var act = () => processor.Process(MakeOriginal(), company);
        await act.Should().ThrowAsync<IOException>();

        txRepo.ClearChangeTracker();
        var knownAfterFailure = await processor.FilterKnownAccessions([
            AmendmentAccession,
            OriginalAccession,
        ]);
        knownAfterFailure.Should().Equal(AmendmentAccession);

        restoreFileManager();
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        txRepo
            .GetByAccessionNumber(OriginalAccession)
            .Should()
            .ContainSingle(t => t.TransactionCode != TransactionCode.Holding);
        txRepo
            .GetByAccessionNumber(AmendmentAccession)
            .Single()
            .SupersededAccessionNumber.Should()
            .Be(OriginalAccession);
    }

    [Fact]
    public async Task Process_AmendmentAgainstLegacyUnknownOriginal_ResolvesFamilyBeforeSuperseding()
    {
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        txRepo.GetByAccessionNumber(OriginalAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        filingRepo.GetByAccessionNumber(OriginalAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        CurrentRows(txRepo).Should().ContainSingle();
        CurrentRows(txRepo).Single().AccessionNumber.Should().Be(AmendmentAccession);
    }

    [Fact]
    public async Task Process_NewerAmendmentAgainstLegacyUnknownOlderAmendment_ReplacesIt()
    {
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps();
        var company = MakeCompany();
        var olderAmendment = MakeAmendment();
        olderAmendment.AccessionNumber = "0001-24-000150";
        olderAmendment.FilingDate = AmendmentFilingDate.AddDays(-1);

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(olderAmendment, company)).Should().BeTrue();
        txRepo.GetByAccessionNumber(olderAmendment.AccessionNumber).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        filingRepo.GetByAccessionNumber(olderAmendment.AccessionNumber).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        await txRepo.SaveChanges();

        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        txRepo.GetAll().Should().ContainSingle();
        txRepo.GetAll().Single().AccessionNumber.Should().Be(AmendmentAccession);
    }

    [Fact]
    public async Task Process_OriginalAfterItsAmendment_IsSkipped()
    {
        // EDGAR's submissions feed lists newest-first, so during a history sweep
        // the 4/A routinely processes BEFORE its Form 4.
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        var result = await processor.Process(MakeOriginal(), company);

        result.Should().BeFalse("the original's rows were already superseded");
        var transactions = CurrentRows(txRepo);
        transactions.Should().ContainSingle();
        transactions[0].AccessionNumber.Should().Be(AmendmentAccession);
        transactions[0].Shares.Should().Be(250);
        transactions[0]
            .SupersededAccessionNumber.Should()
            .Be(
                OriginalAccession,
                "the orphaned amendment claims the original so future sweeps drop it without a fetch"
            );
        AssertCurrentMarker(txRepo, OriginalAccession);
    }

    [Fact]
    public async Task Process_OriginalAfterLegacyUnknownOrphanAmendment_ResolvesAndClaimsIt()
    {
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();
        var amendment = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        amendment.FilingForm = InsiderOwnershipForm.Unknown;
        amendment.SupersededAccessionNumber = null;
        filingRepo.GetByAccessionNumber(AmendmentAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company))
            .Should()
            .BeFalse("the cached Form 4/A proves the legacy orphan superseded this original");

        CurrentRows(txRepo).Should().ContainSingle();
        var remaining = CurrentRows(txRepo).Single();
        remaining.AccessionNumber.Should().Be(AmendmentAccession);
        remaining.FilingForm.Should().Be(InsiderOwnershipForm.Form4);
        remaining.SupersededAccessionNumber.Should().Be(OriginalAccession);
        AssertCurrentMarker(txRepo, OriginalAccession);
    }

    [Fact]
    public async Task Process_OriginalAfterLegacyUnknownAmendmentWithCorruptCache_StillIngests()
    {
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();
        var amendment = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        amendment.FilingForm = InsiderOwnershipForm.Unknown;
        amendment.SupersededAccessionNumber = null;
        var stored = filingRepo.GetByAccessionNumber(AmendmentAccession).Single();
        stored.FilingForm = InsiderOwnershipForm.Unknown;
        stored.Content.FileContent.Bytes = [1, 2, 3];
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company))
            .Should()
            .BeTrue("an unreadable legacy cache cannot block live ingestion");

        txRepo
            .GetAll()
            .Select(t => t.AccessionNumber)
            .Should()
            .BeEquivalentTo([AmendmentAccession, OriginalAccession]);
        amendment.FilingForm.Should().Be(InsiderOwnershipForm.Unknown);
    }

    [Fact]
    public async Task Process_OriginalAfterLegacyUnknownAmendmentWithoutCache_LogsAndStillIngests()
    {
        var logger = Substitute.For<ILogger<InsiderTradingFilingProcessor>>();
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps(logger);
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();
        txRepo.GetByAccessionNumber(AmendmentAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        filingRepo.Delete(filingRepo.GetByAccessionNumber(AmendmentAccession).Single());
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        ShouldHaveFamilyResolutionWarning(logger, AmendmentAccession);
        txRepo.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task Process_OriginalAfterLegacyUnknownAmendmentWithNonOwnershipCache_LogsAndStillIngests()
    {
        var logger = Substitute.For<ILogger<InsiderTradingFilingProcessor>>();
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps(logger);
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();
        txRepo.GetByAccessionNumber(AmendmentAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        var stored = filingRepo.GetByAccessionNumber(AmendmentAccession).Single();
        stored.FilingForm = InsiderOwnershipForm.Unknown;
        stored.Content.FileContent.Bytes = GzipCompressor.Compress(
            Encoding.UTF8.GetBytes("<html><body>not an ownership filing</body></html>")
        );
        await txRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        ShouldHaveFamilyResolutionWarning(logger, AmendmentAccession);
        txRepo.GetAll().Should().HaveCount(2);
    }

    private static void ShouldHaveFamilyResolutionWarning(
        ILogger<InsiderTradingFilingProcessor> logger,
        string accessionNumber
    )
    {
        logger
            .ReceivedCalls()
            .Should()
            .Contain(call =>
                call.GetMethodInfo().Name == nameof(ILogger.Log)
                && Equals(call.GetArguments()[0], LogLevel.Warning)
                && call.GetArguments()[2].ToString()!.Contains(accessionNumber)
            );
    }

    [Fact]
    public async Task Process_FormFiveAmendmentAfterSameDayFormFourAndFive_ReplacesOnlyFormFive()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(500, null, "5"));
        (await processor.Process(MakeFormFiveOriginal(), company)).Should().BeTrue();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(550, OriginalFilingDate, "5/A"));
        (await processor.Process(MakeFormFiveAmendment(), company)).Should().BeTrue();

        var transactions = CurrentRows(txRepo).OrderBy(t => t.AccessionNumber).ToList();
        transactions.Should().HaveCount(2);
        transactions
            .Select(t => t.AccessionNumber)
            .Should()
            .Equal(OriginalAccession, FormFiveAmendmentAccession);
        transactions
            .Single(t => t.FilingForm == InsiderOwnershipForm.Form4)
            .Shares.Should()
            .Be(1000);
        transactions
            .Single(t => t.FilingForm == InsiderOwnershipForm.Form5)
            .Shares.Should()
            .Be(550);
    }

    [Fact]
    public async Task Process_FormFiveAmendmentBeforeSameDayFormFourAndFive_ClaimsOnlyFormFive()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(550, OriginalFilingDate, "5/A"));
        (await processor.Process(MakeFormFiveAmendment(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(500, null, "5"));
        (await processor.Process(MakeFormFiveOriginal(), company))
            .Should()
            .BeFalse("the already-ingested Form 5/A superseded this Form 5");

        var transactions = CurrentRows(txRepo).OrderBy(t => t.AccessionNumber).ToList();
        transactions.Should().HaveCount(2);
        transactions
            .Select(t => t.AccessionNumber)
            .Should()
            .Equal(OriginalAccession, FormFiveAmendmentAccession);
        transactions
            .Single(t => t.FilingForm == InsiderOwnershipForm.Form4)
            .Shares.Should()
            .Be(1000);
        transactions
            .Single(t => t.FilingForm == InsiderOwnershipForm.Form5)
            .SupersededAccessionNumber.Should()
            .Be(FormFiveOriginalAccession);
    }

    [Fact]
    public async Task Process_OlderFormFourAmendmentAfterNewerFormFiveAmendment_IsNotStale()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(550, OriginalFilingDate, "5/A"));
        (await processor.Process(MakeFormFiveAmendment(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company))
            .Should()
            .BeTrue("a newer Form 5/A cannot make a Form 4/A stale");

        txRepo
            .GetAll()
            .Select(t => t.FilingForm)
            .ToList()
            .Should()
            .BeEquivalentTo([InsiderOwnershipForm.Form4, InsiderOwnershipForm.Form5]);
    }

    [Fact]
    public async Task Process_NewerFormFiveAmendment_ReplacesOnlyOlderFormFiveAmendment()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var olderFormFiveAmendment = MakeFormFiveAmendment();
        olderFormFiveAmendment.AccessionNumber = "0001-24-000350";
        olderFormFiveAmendment.FilingDate = AmendmentFilingDate.AddDays(-1);
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(525, OriginalFilingDate, "5/A"));
        (await processor.Process(olderFormFiveAmendment, company)).Should().BeTrue();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(550, OriginalFilingDate, "5/A"));
        (await processor.Process(MakeFormFiveAmendment(), company)).Should().BeTrue();

        var transactions = txRepo.GetAll().OrderBy(t => t.AccessionNumber).ToList();
        transactions.Should().HaveCount(2);
        transactions
            .Select(t => t.AccessionNumber)
            .Should()
            .Equal(AmendmentAccession, FormFiveAmendmentAccession);
    }

    [Fact]
    public async Task Process_FormFiveOriginalAfterBadCrossFamilyClaim_ReassignsTheClaim()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(550, OriginalFilingDate, "5/A"));
        (await processor.Process(MakeFormFiveAmendment(), company)).Should().BeTrue();
        var amendment = txRepo.GetByAccessionNumber(FormFiveAmendmentAccession).Single();
        amendment.SupersededAccessionNumber = OriginalAccession;
        await txRepo.SaveChanges();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(500, null, "5"));
        (await processor.Process(MakeFormFiveOriginal(), company))
            .Should()
            .BeFalse("the Form 5/A is reassigned from its disproved Form 4 claim");

        AssertCurrentMarker(txRepo, FormFiveOriginalAccession);
        txRepo
            .GetByAccessionNumber(FormFiveAmendmentAccession)
            .Single()
            .SupersededAccessionNumber.Should()
            .Be(FormFiveOriginalAccession);
    }

    [Fact]
    public async Task Process_NewerFormFiveAmendment_DoesNotInheritBadCrossFamilyClaim()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();

        var olderAmendment = MakeFormFiveAmendment();
        olderAmendment.AccessionNumber = "0001-24-000350";
        olderAmendment.FilingDate = AmendmentFilingDate.AddDays(-1);
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(525, OriginalFilingDate, "5/A"));
        (await processor.Process(olderAmendment, company)).Should().BeTrue();
        txRepo
            .GetByAccessionNumber(olderAmendment.AccessionNumber)
            .Single()
            .SupersededAccessionNumber = OriginalAccession;
        await txRepo.SaveChanges();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(550, OriginalFilingDate, "5/A"));
        (await processor.Process(MakeFormFiveAmendment(), company)).Should().BeTrue();

        txRepo.GetByAccessionNumber(olderAmendment.AccessionNumber).Should().BeEmpty();
        txRepo
            .GetByAccessionNumber(FormFiveAmendmentAccession)
            .Single()
            .SupersededAccessionNumber.Should()
            .BeNull("the stored target is authoritatively Form 4, not Form 5");
    }

    [Fact]
    public async Task Process_LegacyCrossFamilyClaim_DoesNotHideOrSuppressOriginal()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(500, null, "5"));
        (await processor.Process(MakeFormFiveOriginal(), company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        var formFiveRows = txRepo.GetByAccessionNumber(FormFiveOriginalAccession).ToList();
        txRepo.Delete(formFiveRows);
        var formFourAmendment = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        formFourAmendment.SupersededAccessionNumber = FormFiveOriginalAccession;
        await txRepo.SaveChanges();

        var known = await processor.FilterKnownAccessions([
            AmendmentAccession,
            FormFiveOriginalAccession,
        ]);
        known
            .Should()
            .Equal(
                [AmendmentAccession],
                "the amendment is known by its own row but cannot carry a cross-family target"
            );

        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildOwnershipXml(500, null, "5"));
        (await processor.Process(MakeFormFiveOriginal(), company))
            .Should()
            .BeTrue("a cross-family claim cannot suppress the real original");

        txRepo.GetByAccessionNumber(FormFiveOriginalAccession).Should().ContainSingle();
        txRepo.GetByAccessionNumber(AmendmentAccession).Should().ContainSingle();
    }

    [Fact]
    public async Task Process_LegacyEmptyParseMarkerClaim_DoesNotHideOriginal()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        txRepo.Delete(txRepo.GetByAccessionNumber(OriginalAccession));
        await txRepo.SaveChanges();

        var legacyMarker = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        legacyMarker.TransactionCode = TransactionCode.Other;
        legacyMarker.SecurityTitle = "No Securities Owned";
        legacyMarker.Shares = 0;
        legacyMarker.ParserVersion = 8;
        await txRepo.SaveChanges();

        var known = await processor.FilterKnownAccessions([OriginalAccession, AmendmentAccession]);
        known.Should().Equal(AmendmentAccession);

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        txRepo.GetByAccessionNumber(OriginalAccession).Should().ContainSingle();
    }

    [Fact]
    public async Task Process_AmbiguousPreV5OtherClaim_DoesNotHideOriginal()
    {
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        txRepo.Delete(txRepo.GetByAccessionNumber(OriginalAccession));
        await txRepo.SaveChanges();

        var legacyOther = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        legacyOther.TransactionCode = TransactionCode.Other;
        legacyOther.ParserVersion = 4;
        await txRepo.SaveChanges();

        var known = await processor.FilterKnownAccessions([OriginalAccession, AmendmentAccession]);
        known.Should().Equal(AmendmentAccession);

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        txRepo.GetByAccessionNumber(OriginalAccession).Should().ContainSingle();
    }

    [Fact]
    public async Task Process_ValidLegacyUnknownClaim_ResolvesFromCachedXmlAndStillSuppressesOriginal()
    {
        var (processor, txRepo, secClient, filingRepo) = CreateProcessorWithDeps();
        var company = MakeCompany();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company)).Should().BeTrue();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        (await processor.Process(MakeAmendment(), company)).Should().BeTrue();

        txRepo.Delete(txRepo.GetByAccessionNumber(OriginalAccession));
        await txRepo.SaveChanges();

        var amendment = txRepo.GetByAccessionNumber(AmendmentAccession).Single();
        amendment.FilingForm = InsiderOwnershipForm.Unknown;
        filingRepo.GetByAccessionNumber(OriginalAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        filingRepo.GetByAccessionNumber(AmendmentAccession).Single().FilingForm =
            InsiderOwnershipForm.Unknown;
        await txRepo.SaveChanges();

        var before = await processor.FilterKnownAccessions([OriginalAccession, AmendmentAccession]);
        before.Should().Equal(AmendmentAccession);

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(MakeOriginal(), company))
            .Should()
            .BeFalse("the cached Form 4/A proves this same-family legacy claim is valid");

        AssertCurrentMarker(txRepo, OriginalAccession);
        txRepo
            .GetByAccessionNumber(AmendmentAccession)
            .Single()
            .FilingForm.Should()
            .Be(InsiderOwnershipForm.Form4);

        var after = await processor.FilterKnownAccessions([OriginalAccession, AmendmentAccession]);
        after.Should().BeEquivalentTo([OriginalAccession, AmendmentAccession]);
    }

    [Fact]
    public async Task Process_AmendmentWithDateShiftedOriginal_StillSupersedesIt()
    {
        // EDGAR indexes an after-17:30 submission the NEXT business day, so the
        // original's feed FilingDate can trail the amendment's filer-entered
        // dateOfOriginalSubmission. The window resolution must still find and
        // replace it — exact-date-only matching would leave both rows counted.
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
        var company = MakeCompany();

        var shiftedOriginal = MakeOriginal();
        shiftedOriginal.FilingDate = OriginalFilingDate.AddDays(3); // Friday 18:00 → Monday index
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(OriginalForm4Xml);
        (await processor.Process(shiftedOriginal, company)).Should().BeTrue();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentForm4Xml);
        var result = await processor.Process(MakeAmendment(), company);

        result.Should().BeTrue();
        var transactions = CurrentRows(txRepo);
        transactions.Should().ContainSingle("the date-shifted original must still be replaced");
        transactions[0].AccessionNumber.Should().Be(AmendmentAccession);
        transactions[0].SupersededAccessionNumber.Should().Be(OriginalAccession);
        AssertCurrentMarker(txRepo, OriginalAccession);
    }

    [Fact]
    public async Task FilterKnownAccessions_SupersededOriginal_CountsAsKnown()
    {
        // A fully superseded original retains a non-section marker of its own.
        // Claims cannot make an original known because a legacy claim may have
        // been paired with wholesale deletion of sections it never restated.
        var (processor, _, secClient, _) = CreateProcessorWithDeps();
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
        var (processor, txRepo, secClient, _) = CreateProcessorWithDeps();
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

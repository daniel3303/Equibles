using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class InsiderTradingFilingProcessorTests {
    // ── SanitizeXml ──

    [Fact]
    public void SanitizeXml_WithSgmlEnvelope_ExtractsInnerXml() {
        var input = """
            <SEC-DOCUMENT>
            <XML>
            <ownershipDocument><root/></ownershipDocument>
            </XML>
            </SEC-DOCUMENT>
            """;

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("<ownershipDocument>");
        result.Should().NotContain("<SEC-DOCUMENT>");
        result.Should().NotContain("<XML>");
    }

    [Fact]
    public void SanitizeXml_WithoutEnvelope_ReturnsXmlAsIs() {
        var input = "<ownershipDocument><root/></ownershipDocument>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("<ownershipDocument>");
    }

    [Fact]
    public void SanitizeXml_UnescapedAmpersands_AreEscaped() {
        var input = "<XML><doc>AT&T Corp & Others</doc></XML>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("AT&amp;T Corp &amp; Others");
    }

    [Fact]
    public void SanitizeXml_AlreadyEscapedEntities_ArePreserved() {
        var input = "<XML><doc>&amp; &lt; &gt; &quot; &apos; &#123; &#x1F;</doc></XML>";

        var result = InsiderTradingFilingProcessor.SanitizeXml(input);

        result.Should().Contain("&amp;");
        result.Should().Contain("&lt;");
        result.Should().Contain("&gt;");
        result.Should().Contain("&quot;");
        result.Should().Contain("&apos;");
        result.Should().Contain("&#123;");
        result.Should().Contain("&#x1F;");
        // Should not double-escape
        result.Should().NotContain("&amp;amp;");
    }

    // ── ParseTransactionCode ──

    [Theory]
    [InlineData("P", TransactionCode.Purchase)]
    [InlineData("S", TransactionCode.Sale)]
    [InlineData("A", TransactionCode.Award)]
    [InlineData("M", TransactionCode.Conversion)]
    [InlineData("X", TransactionCode.Exercise)]
    [InlineData("F", TransactionCode.TaxPayment)]
    [InlineData("E", TransactionCode.Expiration)]
    [InlineData("G", TransactionCode.Gift)]
    [InlineData("I", TransactionCode.Inheritance)]
    [InlineData("W", TransactionCode.Discretionary)]
    public void ParseTransactionCode_ValidCode_ReturnsCorrectEnum(string code, TransactionCode expected) {
        InsiderTradingFilingProcessor.ParseTransactionCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("p", TransactionCode.Purchase)]
    [InlineData("s", TransactionCode.Sale)]
    public void ParseTransactionCode_LowerCase_StillParses(string code, TransactionCode expected) {
        InsiderTradingFilingProcessor.ParseTransactionCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Z")]
    [InlineData("PURCHASE")]
    public void ParseTransactionCode_InvalidOrNull_ReturnsOther(string code) {
        InsiderTradingFilingProcessor.ParseTransactionCode(code).Should().Be(TransactionCode.Other);
    }

    // ── ParseBool ──

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("yes", false)]
    public void ParseBool_VariousInputs_ReturnsExpected(string input, bool expected) {
        InsiderTradingFilingProcessor.ParseBool(input).Should().Be(expected);
    }

    // ── ParseLong ──

    [Theory]
    [InlineData("1000", 1000L)]
    [InlineData("0", 0L)]
    [InlineData("-500", -500L)]
    public void ParseLong_IntegerValues_ParsesCorrectly(string input, long expected) {
        InsiderTradingFilingProcessor.ParseLong(input).Should().Be(expected);
    }

    [Fact]
    public void ParseLong_DecimalValue_TruncatesToLong() {
        // "1234.5678" can't be parsed as long, falls back to ParseDecimal then casts
        InsiderTradingFilingProcessor.ParseLong("1234.5678").Should().Be(1234L);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParseLong_InvalidOrNull_ReturnsZero(string input) {
        InsiderTradingFilingProcessor.ParseLong(input).Should().Be(0L);
    }

    // ── ParseDecimal ──

    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData("0", 0)]
    [InlineData("-99.99", -99.99)]
    [InlineData("1,234.56", 1234.56)]
    public void ParseDecimal_ValidInput_ReturnsValue(string input, double expected) {
        InsiderTradingFilingProcessor.ParseDecimal(input).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParseDecimal_InvalidOrNull_ReturnsZero(string input) {
        InsiderTradingFilingProcessor.ParseDecimal(input).Should().Be(0m);
    }

    // ── CanProcess ──

    [Fact]
    public void CanProcess_FormFour_ReturnsTrue() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.FormFour).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_FormThree_ReturnsTrue() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.FormThree).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_TenK_ReturnsFalse() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.TenK).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_EightK_ReturnsFalse() {
        var processor = CreateProcessor();
        processor.CanProcess(DocumentType.EightK).Should().BeFalse();
    }

    // ErrorReporter not needed — only testing CanProcess and static helpers
    private static InsiderTradingFilingProcessor CreateProcessor() {
        return new InsiderTradingFilingProcessor(
            NSubstitute.Substitute.For<IServiceScopeFactory>(),
            NSubstitute.Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            null);
    }

    // ── Process ─────────────────────────────────────────────────────────

    private static readonly string ValidForm4Xml = """
        <ownershipDocument>
            <reportingOwner>
                <reportingOwnerId>
                    <rptOwnerCik>0001234567</rptOwnerCik>
                    <rptOwnerName>John Doe</rptOwnerName>
                </reportingOwnerId>
                <reportingOwnerAddress>
                    <rptOwnerCity>Cupertino</rptOwnerCity>
                    <rptOwnerStateOrCountry>CA</rptOwnerStateOrCountry>
                </reportingOwnerAddress>
                <reportingOwnerRelationship>
                    <isDirector>1</isDirector>
                    <isOfficer>true</isOfficer>
                    <officerTitle>CEO</officerTitle>
                    <isTenPercentOwner>0</isTenPercentOwner>
                </reportingOwnerRelationship>
            </reportingOwner>
            <nonDerivativeTable>
                <nonDerivativeTransaction>
                    <securityTitle><value>Common Stock</value></securityTitle>
                    <transactionDate><value>2024-03-15</value></transactionDate>
                    <transactionCoding><transactionCode>P</transactionCode></transactionCoding>
                    <transactionAmounts>
                        <transactionShares><value>1000</value></transactionShares>
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

    private static readonly string Form3HoldingsXml = """
        <ownershipDocument>
            <reportingOwner>
                <reportingOwnerId>
                    <rptOwnerCik>0009999999</rptOwnerCik>
                    <rptOwnerName>Jane Smith</rptOwnerName>
                </reportingOwnerId>
                <reportingOwnerRelationship>
                    <isDirector>0</isDirector>
                    <isOfficer>0</isOfficer>
                    <isTenPercentOwner>1</isTenPercentOwner>
                </reportingOwnerRelationship>
            </reportingOwner>
            <nonDerivativeTable>
                <nonDerivativeHolding>
                    <securityTitle><value>Common Stock</value></securityTitle>
                    <postTransactionAmounts>
                        <sharesOwnedFollowingTransaction><value>10000</value></sharesOwnedFollowingTransaction>
                    </postTransactionAmounts>
                    <ownershipNature>
                        <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                    </ownershipNature>
                </nonDerivativeHolding>
            </nonDerivativeTable>
        </ownershipDocument>
        """;

    private (InsiderTradingFilingProcessor processor, InsiderOwnerRepository ownerRepo, InsiderTransactionRepository txRepo, ISecEdgarClient secClient)
        CreateProcessorWithDeps() {
        var dbContext = TestDbContextFactory.Create(
            new InsiderTradingModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new ErrorsModuleConfiguration());

        var ownerRepo = new InsiderOwnerRepository(dbContext);
        var txRepo = new InsiderTransactionRepository(dbContext);
        var errorRepo = new ErrorRepository(dbContext);
        var errorManager = new ErrorManager(errorRepo);
        var secClient = Substitute.For<ISecEdgarClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(InsiderOwnerRepository), ownerRepo),
            (typeof(InsiderTransactionRepository), txRepo),
            (typeof(ErrorManager), errorManager));

        var errorReporter = new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>());
        var processor = new InsiderTradingFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            errorReporter);

        return (processor, ownerRepo, txRepo, secClient);
    }

    private static FilingData MakeFiling(string accession = null, string form = "4") {
        accession ??= $"0001-24-{Guid.NewGuid().ToString("N")[..6]}";
        return new FilingData {
            AccessionNumber = accession,
            Form = form,
            FilingDate = new DateOnly(2024, 3, 16),
            ReportDate = new DateOnly(2024, 3, 15),
            Cik = "0000320193",
        };
    }

    private static CommonStock MakeCompany() {
        return new CommonStock { Ticker = "AAPL", Name = "Apple Inc", Cik = "0000320193" };
    }

    [Fact]
    public async Task Process_ValidForm4_InsertsTransactionsAndOwner() {
        var (processor, ownerRepo, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidForm4Xml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().HaveCount(1);
        transactions[0].TransactionCode.Should().Be(TransactionCode.Purchase);
        transactions[0].Shares.Should().Be(1000);
        transactions[0].PricePerShare.Should().Be(150.50m);
        transactions[0].SharesOwnedAfter.Should().Be(5000);
        transactions[0].OwnershipNature.Should().Be(OwnershipNature.Direct);

        var owners = ownerRepo.GetAll().ToList();
        owners.Should().HaveCount(1);
        owners[0].Name.Should().Be("John Doe");
        owners[0].IsDirector.Should().BeTrue();
        owners[0].IsOfficer.Should().BeTrue();
        owners[0].OfficerTitle.Should().Be("CEO");
    }

    [Fact]
    public async Task Process_Form3Holdings_InsertsHoldingsAsTransactions() {
        var (processor, ownerRepo, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(Form3HoldingsXml);
        var filing = MakeFiling(form: "3");

        var result = await processor.Process(filing, MakeCompany());

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().HaveCount(1);
        transactions[0].TransactionCode.Should().Be(TransactionCode.Other);
        transactions[0].Shares.Should().Be(10000);
        transactions[0].SharesOwnedAfter.Should().Be(10000);
    }

    [Fact]
    public async Task Process_AlreadyImported_ReturnsFalse() {
        var (processor, ownerRepo, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidForm4Xml);
        var filing = MakeFiling();

        // First import
        await processor.Process(filing, MakeCompany());

        // Second import of same accession
        var result = await processor.Process(filing, MakeCompany());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Process_EmptyContent_ReturnsFalse() {
        var (processor, _, _, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns("");

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Process_MalformedXml_ReturnsFalse() {
        var (processor, _, _, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns("<not>valid<xml");

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Process_MissingReportingOwner_ReturnsFalse() {
        var (processor, _, _, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(
            "<ownershipDocument><nonDerivativeTable/></ownershipDocument>");

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Process_MissingOwnerCik_ReturnsFalse() {
        var xml = """
            <ownershipDocument>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
                <nonDerivativeTable/>
            </ownershipDocument>
            """;
        var (processor, _, _, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(xml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Process_ExistingOwnerReused_NoNewOwnerCreated() {
        var (processor, ownerRepo, txRepo, secClient) = CreateProcessorWithDeps();

        // Pre-create the owner
        ownerRepo.Add(new InsiderOwner { OwnerCik = "0001234567", Name = "John Doe" });
        await ownerRepo.SaveChanges();

        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidForm4Xml);

        await processor.Process(MakeFiling(), MakeCompany());

        var owners = ownerRepo.GetAll().ToList();
        owners.Should().HaveCount(1);
    }

    [Fact]
    public async Task Process_NoTransactions_SavesMarkerAndReturnsTrue() {
        var xml = """
            <ownershipDocument>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001234567</rptOwnerCik>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
            </ownershipDocument>
            """;
        var (processor, _, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(xml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var markers = txRepo.GetAll().ToList();
        markers.Should().HaveCount(1);
        markers[0].SecurityTitle.Should().Be("No Securities Owned");
    }

    [Fact]
    public async Task Process_AmendmentFiling_InsertsAsNewRecord() {
        var (processor, ownerRepo, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidForm4Xml);
        var company = MakeCompany();

        // First import
        await processor.Process(MakeFiling(accession: "0001-24-000001"), company);
        txRepo.GetAll().Should().HaveCount(1);

        // Amendment — stored as a separate record (different accession number)
        var amendedXml = ValidForm4Xml.Replace("<value>1000</value>", "<value>2000</value>");
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(amendedXml);
        var amendFiling = MakeFiling(accession: "0001-24-000002", form: "4/A");

        await processor.Process(amendFiling, company);

        var transactions = txRepo.GetAll().OrderBy(t => t.AccessionNumber).ToList();
        transactions.Should().HaveCount(2);
        transactions[0].Shares.Should().Be(1000);
        transactions[0].AccessionNumber.Should().Be("0001-24-000001");
        transactions[1].Shares.Should().Be(2000);
        transactions[1].AccessionNumber.Should().Be("0001-24-000002");
        transactions[1].IsAmendment.Should().BeTrue();
    }

    [Fact]
    public async Task Process_SgmlEnvelope_StrippedBeforeParsing() {
        var wrappedXml = $"<SEC-DOCUMENT>\n<XML>\n{ValidForm4Xml}\n</XML>\n</SEC-DOCUMENT>";
        var (processor, _, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(wrappedXml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        txRepo.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public async Task Process_Form4WithDerivativeTransaction_InsertsTransactionFromDerivativeTable() {
        // Form 4 splits transactions across two parallel sections: <nonDerivativeTable> for
        // common stock (already covered by Process_ValidForm4_InsertsTransactionsAndOwner) and
        // <derivativeTable> for stock options, warrants, and similar instruments. The latter
        // branch — `root.Element("derivativeTable")` in InsiderTradingFilingProcessor.Process
        // — is not exercised by any existing test, yet executive stock options are an
        // everyday Form 4 payload. A regression that broke just the derivative branch
        // (e.g. someone narrowing the ParseTransaction caller to nonDerivative only) would
        // silently lose option-grant data without dropping the surrounding stock trades.
        //
        // This `[Fact]` ships a Form 4 containing ONLY a derivativeTransaction — no
        // nonDerivativeTable, no nonDerivativeTransaction — and asserts: (a) Process
        // returns true (parse succeeded, not the empty "No Securities Owned" fallback),
        // (b) one transaction is persisted, (c) SecurityTitle preserves the derivative
        // instrument name ("Stock Option (Right to Buy)") which distinguishes it from a
        // common-stock row.
        var derivativeForm4Xml = """
            <ownershipDocument>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001234567</rptOwnerCik>
                        <rptOwnerName>John Doe</rptOwnerName>
                    </reportingOwnerId>
                    <reportingOwnerRelationship>
                        <isOfficer>1</isOfficer>
                        <officerTitle>CEO</officerTitle>
                    </reportingOwnerRelationship>
                </reportingOwner>
                <derivativeTable>
                    <derivativeTransaction>
                        <securityTitle><value>Stock Option (Right to Buy)</value></securityTitle>
                        <transactionDate><value>2024-03-15</value></transactionDate>
                        <transactionCoding><transactionCode>A</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>2000</value></transactionShares>
                            <transactionPricePerShare><value>0</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>2000</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </derivativeTransaction>
                </derivativeTable>
            </ownershipDocument>
            """;
        var (processor, _, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(derivativeForm4Xml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().ContainSingle();
        transactions[0].SecurityTitle.Should().Be("Stock Option (Right to Buy)");
        transactions[0].Shares.Should().Be(2000);
        transactions[0].TransactionCode.Should().Be(TransactionCode.Award);
    }

    [Fact]
    public async Task Process_Form3WithDerivativeHolding_InsertsHoldingFromDerivativeTable() {
        // Companion to Process_Form4WithDerivativeTransaction. Inside `<derivativeTable>`
        // there are two element kinds — `<derivativeTransaction>` (covered) and
        // `<derivativeHolding>` (NOT covered). The holdings loop at line 146 of
        // InsiderTradingFilingProcessor.Process is what initial-statement Form 3 filings
        // rely on: a newly-onboarding executive who already owns stock options reports them
        // as derivativeHoldings (no transactionDate, no acquired/disposed code — just "this
        // is what I currently hold"). `ParseHolding` synthesises a record using
        // `filing.ReportDate` as the TransactionDate, hard-codes TransactionCode.Other and
        // AcquiredDisposed.Acquired, and lifts Shares from `postTransactionAmounts`.
        //
        // A regression that dropped this branch — or accidentally narrowed the inner foreach
        // to `derivativeTransaction` only — would silently flatten every newly-onboarding
        // executive's option portfolio on Form 3 filings. The existing
        // Process_Form3Holdings_InsertsHoldingsAsTransactions test covers the
        // **nonDerivative** holding path; this one covers the derivative path.
        var derivativeHoldingForm3Xml = """
            <ownershipDocument>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0007777777</rptOwnerCik>
                        <rptOwnerName>Jane Doe</rptOwnerName>
                    </reportingOwnerId>
                    <reportingOwnerRelationship>
                        <isOfficer>1</isOfficer>
                        <officerTitle>CFO</officerTitle>
                    </reportingOwnerRelationship>
                </reportingOwner>
                <derivativeTable>
                    <derivativeHolding>
                        <securityTitle><value>Stock Option (Right to Buy)</value></securityTitle>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>5000</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </derivativeHolding>
                </derivativeTable>
            </ownershipDocument>
            """;
        var (processor, _, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(derivativeHoldingForm3Xml);
        var filing = MakeFiling(form: "3");

        var result = await processor.Process(filing, MakeCompany());

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().ToList();
        transactions.Should().ContainSingle();
        transactions[0].SecurityTitle.Should().Be("Stock Option (Right to Buy)");
        transactions[0].Shares.Should().Be(5000);
        // ParseHolding hard-codes TransactionCode = Other (no transaction, just a held position).
        transactions[0].TransactionCode.Should().Be(TransactionCode.Other);
        // TransactionDate falls back to filing.ReportDate because <derivativeHolding> has no
        // <transactionDate> element of its own.
        transactions[0].TransactionDate.Should().Be(filing.ReportDate);
    }

    [Fact]
    public async Task Process_Form4WithDirectAndIndirectOwnershipOfSameSecurity_PersistsBothRecords() {
        // A single Form 4 can report the same (security, date, code) twice when an insider
        // holds the position both directly (own account) and indirectly (through a trust,
        // joint account, etc.). Under SEC rules these are distinct beneficial ownerships
        // with their own running balances, so both rows must persist — the dedup key
        // must include OwnershipNature (and SharesOwnedAfter) to keep them apart.
        var dualOwnershipForm4Xml = """
            <ownershipDocument>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0008888888</rptOwnerCik>
                        <rptOwnerName>Alice Roe</rptOwnerName>
                    </reportingOwnerId>
                    <reportingOwnerRelationship>
                        <isOfficer>1</isOfficer>
                        <officerTitle>CTO</officerTitle>
                    </reportingOwnerRelationship>
                </reportingOwner>
                <nonDerivativeTable>
                    <nonDerivativeTransaction>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <transactionDate><value>2024-05-10</value></transactionDate>
                        <transactionCoding><transactionCode>S</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>500</value></transactionShares>
                            <transactionPricePerShare><value>150.00</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>1000</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeTransaction>
                    <nonDerivativeTransaction>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <transactionDate><value>2024-05-10</value></transactionDate>
                        <transactionCoding><transactionCode>S</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>500</value></transactionShares>
                            <transactionPricePerShare><value>150.00</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>2000</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>I</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeTransaction>
                </nonDerivativeTable>
            </ownershipDocument>
            """;
        var (processor, _, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(dualOwnershipForm4Xml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().OrderBy(t => t.OwnershipNature).ToList();
        // Direct and Indirect are separate beneficial ownerships under SEC rules — they
        // must be persisted as two distinct rows (their SharesOwnedAfter balances and
        // OwnershipNature differ).
        transactions.Should().HaveCount(2);
        transactions[0].OwnershipNature.Should().Be(OwnershipNature.Direct);
        transactions[0].SharesOwnedAfter.Should().Be(1000);
        transactions[1].OwnershipNature.Should().Be(OwnershipNature.Indirect);
        transactions[1].SharesOwnedAfter.Should().Be(2000);
    }

    [Fact]
    public async Task Process_Form4WithMultiplePurchasesSameDay_PersistsAllTransactions() {
        // Real-world filing: Joel Marcus (ARE) 2026-05-05, accession 0001216955-26-000015
        // reports three open-market purchases on the same day, broken out by price tranche.
        // All three share (insider, security, date, code P, accession, ownership Direct);
        // they differ only in Shares, PricePerShare, and the running SharesOwnedAfter.
        // A dedup key that collapses on the narrow tuple silently drops the 2nd and 3rd
        // tranches — the user-visible bug on https://equibles.com/stocks/are/insidertrading.
        var multiPurchaseXml = """
            <ownershipDocument>
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>0001216955</rptOwnerCik>
                        <rptOwnerName>MARCUS JOEL S</rptOwnerName>
                    </reportingOwnerId>
                    <reportingOwnerRelationship>
                        <isDirector>1</isDirector>
                        <isOfficer>1</isOfficer>
                        <officerTitle>Executive Chairman</officerTitle>
                    </reportingOwnerRelationship>
                </reportingOwner>
                <nonDerivativeTable>
                    <nonDerivativeTransaction>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <transactionDate><value>2026-05-05</value></transactionDate>
                        <transactionCoding><transactionCode>P</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>2062</value></transactionShares>
                            <transactionPricePerShare><value>41.89</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>574786</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeTransaction>
                    <nonDerivativeTransaction>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <transactionDate><value>2026-05-05</value></transactionDate>
                        <transactionCoding><transactionCode>P</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>3832</value></transactionShares>
                            <transactionPricePerShare><value>42.76</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>578618</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeTransaction>
                    <nonDerivativeTransaction>
                        <securityTitle><value>Common Stock</value></securityTitle>
                        <transactionDate><value>2026-05-05</value></transactionDate>
                        <transactionCoding><transactionCode>P</transactionCode></transactionCoding>
                        <transactionAmounts>
                            <transactionShares><value>1606</value></transactionShares>
                            <transactionPricePerShare><value>43.70</value></transactionPricePerShare>
                            <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
                        </transactionAmounts>
                        <postTransactionAmounts>
                            <sharesOwnedFollowingTransaction><value>580224</value></sharesOwnedFollowingTransaction>
                        </postTransactionAmounts>
                        <ownershipNature>
                            <directOrIndirectOwnership><value>D</value></directOrIndirectOwnership>
                        </ownershipNature>
                    </nonDerivativeTransaction>
                </nonDerivativeTable>
            </ownershipDocument>
            """;
        var (processor, _, txRepo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(multiPurchaseXml);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var transactions = txRepo.GetAll().OrderBy(t => t.Shares).ToList();
        transactions.Should().HaveCount(3);
        transactions[0].Shares.Should().Be(1606);
        transactions[0].PricePerShare.Should().Be(43.70m);
        transactions[0].SharesOwnedAfter.Should().Be(580224);
        transactions[1].Shares.Should().Be(2062);
        transactions[1].PricePerShare.Should().Be(41.89m);
        transactions[1].SharesOwnedAfter.Should().Be(574786);
        transactions[2].Shares.Should().Be(3832);
        transactions[2].PricePerShare.Should().Be(42.76m);
        transactions[2].SharesOwnedAfter.Should().Be(578618);
    }
}

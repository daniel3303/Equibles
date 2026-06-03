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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class Form144FilingProcessorTests
{
    // ── CanProcess ──

    [Fact]
    public void CanProcess_Form144_ReturnsTrue()
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.Form144).Should().BeTrue();
    }

    [Theory]
    [InlineData("FormFour")]
    [InlineData("FormThree")]
    [InlineData("TenK")]
    public void CanProcess_OtherTypes_ReturnsFalse(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeFalse();
    }

    // ── ParseDate ──

    [Theory]
    [InlineData("05/27/2026", 2026, 5, 27)]
    [InlineData("8/1/2002", 2002, 8, 1)]
    public void ParseDate_UsFormat_Parses(string input, int y, int m, int d)
    {
        Form144FilingProcessor.ParseDate(input).Should().Be(new DateOnly(y, m, d));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026-05-27")] // ISO is not the Form 144 wire format
    public void ParseDate_InvalidOrEmpty_ReturnsNull(string input)
    {
        Form144FilingProcessor.ParseDate(input).Should().BeNull();
    }

    // ── ParseLong / ParseDecimal ──

    [Fact]
    public void ParseLong_DecimalString_TruncatesToLong()
    {
        Form144FilingProcessor.ParseLong("50000.00").Should().Be(50000);
        Form144FilingProcessor.ParseLong("14687356000").Should().Be(14687356000);
        Form144FilingProcessor.ParseLong("").Should().Be(0);
    }

    [Fact]
    public void ParseDecimal_InvariantCulture_Parses()
    {
        Form144FilingProcessor.ParseDecimal("15551085.00").Should().Be(15551085.00m);
        Form144FilingProcessor.ParseDecimal("bad").Should().Be(0);
    }

    // ── SanitizeXml ──

    [Fact]
    public void SanitizeXml_WithSgmlEnvelope_ExtractsInnerXml()
    {
        var result = Form144FilingProcessor.SanitizeXml(ValidForm144Submission);
        result.Should().StartWith("<?xml").And.Contain("<edgarSubmission");
        result.Should().NotContain("<SEC-DOCUMENT>");
    }

    // ── Process ──

    [Fact]
    public async Task Process_ValidForm144_InsertsFilingWithPriorSale()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidForm144Submission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().Include(f => f.PriorSales).SingleAsync();
        filing.SellerName.Should().Be("LEVINSON ARTHUR D");
        filing.RelationshipToIssuer.Should().Be("Director");
        filing.SecurityClassTitle.Should().Be("Common");
        filing.BrokerName.Should().Be("Charles Schwab & Co., Inc.");
        filing.SharesToBeSold.Should().Be(50000);
        filing.AggregateMarketValue.Should().Be(15551085.00m);
        filing.SharesOutstanding.Should().Be(14687356000);
        filing.ApproxSaleDate.Should().Be(new DateOnly(2026, 5, 27));
        filing.SecuritiesExchangeName.Should().Be("NASDAQ");
        filing.Remarks.Should().Contain("HOMEPLACE TRUST");

        filing.PriorSales.Should().ContainSingle();
        filing.PriorSales[0].AmountSold.Should().Be(250000);
        filing.PriorSales[0].GrossProceeds.Should().Be(71190164.00m);
        filing.PriorSales[0].SaleDate.Should().Be(new DateOnly(2026, 5, 6));
    }

    [Fact]
    public async Task Process_LongAdrSecurityClassTitle_StoresFullTitle()
    {
        // Foreign issuers (e.g. Banco Santander) report a long ADR legal description as the
        // securities class title — 144 chars here. The column was widened to 512 so it persists
        // in full instead of overflowing the old 128-char limit and failing the whole INSERT.
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(LongAdrTitleSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.SecurityClassTitle.Should().Be(LongAdrClassTitle);
        filing.SecurityClassTitle.Length.Should().Be(144);
    }

    [Fact]
    public async Task Process_OversizedSecurityClassTitle_TruncatesToColumnLength()
    {
        // Safety net: a class title longer than the 512-char column is capped on the way in so a
        // single oversized free-text field can never fail the filing's INSERT again.
        var oversizedTitle = new string('X', 600);
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(BuildSubmissionWithClassTitle(oversizedTitle));

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.SecurityClassTitle.Should().Be(new string('X', 512));
    }

    [Fact]
    public async Task Process_MultipleRelationships_JoinsWithComma()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(MultiRelationshipSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.RelationshipToIssuer.Should().Be("Director, Officer");
    }

    [Fact]
    public async Task Process_NothingToReportPriorSales_StoresNoChildRows()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(NothingToReportSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().Include(f => f.PriorSales).SingleAsync();
        filing.PriorSales.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_DuplicateAccession_IsNotInsertedTwice()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidForm144Submission);
        var filing = MakeFiling();

        var first = await processor.Process(filing, MakeCompany());
        var second = await processor.Process(filing, MakeCompany());

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await repo.GetAll().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Process_EmptyContent_ReturnsFalse()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns("");

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeFalse();
        (await repo.GetAll().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Process_NonXmlContent_ReturnsFalse()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns("<SEC-DOCUMENT>legacy text</SEC-DOCUMENT>");

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeFalse();
        (await repo.GetAll().AnyAsync()).Should().BeFalse();
    }

    // ── Helpers ──

    private static (
        Form144FilingProcessor processor,
        Form144FilingRepository repo,
        ISecEdgarClient secClient
    ) CreateProcessorWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new InsiderTradingModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var repo = new Form144FilingRepository(dbContext);
        var errorRepo = new ErrorRepository(dbContext);
        var errorManager = new ErrorManager(errorRepo);
        var secClient = Substitute.For<ISecEdgarClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(Form144FilingRepository), repo),
            (typeof(ErrorManager), errorManager)
        );

        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var processor = new Form144FilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<Form144FilingProcessor>>(),
            errorReporter
        );

        return (processor, repo, secClient);
    }

    private static FilingData MakeFiling(string accession = null)
    {
        accession ??= $"0001921094-26-{Guid.NewGuid().ToString("N")[..6]}";
        return new FilingData
        {
            AccessionNumber = accession,
            Form = "144",
            FilingDate = new DateOnly(2026, 5, 27),
            ReportDate = new DateOnly(2026, 5, 27),
            Cik = "0000320193",
        };
    }

    private static CommonStock MakeCompany()
    {
        return new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
    }

    // A real Apple Form 144 submission (accession 0001921094-26-000555), trimmed to the
    // SGML envelope plus the <XML> ownership body the processor reads.
    private const string ValidForm144Submission = """
        <SEC-DOCUMENT>0001921094-26-000555.txt : 20260527
        <SEC-HEADER>...
        <DOCUMENT>
        <TYPE>144
        <XML>
        <?xml version="1.0" encoding="UTF-8"?><edgarSubmission xmlns="http://www.sec.gov/edgar/ownership" xmlns:com="http://www.sec.gov/edgar/common">
          <headerData>
            <submissionType>144</submissionType>
          </headerData>
          <formData>
            <issuerInfo>
              <issuerCik>0000320193</issuerCik>
              <issuerName>Apple Inc.</issuerName>
              <nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>LEVINSON ARTHUR D</nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>
              <relationshipsToIssuer>
                <relationshipToIssuer>Director</relationshipToIssuer>
              </relationshipsToIssuer>
            </issuerInfo>
            <securitiesInformation>
              <securitiesClassTitle>Common</securitiesClassTitle>
              <brokerOrMarketmakerDetails>
                <name>Charles Schwab &amp; Co., Inc.</name>
              </brokerOrMarketmakerDetails>
              <noOfUnitsSold>50000</noOfUnitsSold>
              <aggregateMarketValue>15551085.00</aggregateMarketValue>
              <noOfUnitsOutstanding>14687356000</noOfUnitsOutstanding>
              <approxSaleDate>05/27/2026</approxSaleDate>
              <securitiesExchangeName>NASDAQ</securitiesExchangeName>
            </securitiesInformation>
            <nothingToReportFlagOnSecuritiesSoldInPast3Months>N</nothingToReportFlagOnSecuritiesSoldInPast3Months>
            <securitiesSoldInPast3Months>
              <sellerDetails>
                <name>ARTHUR D LEVINSON</name>
              </sellerDetails>
              <securitiesClassTitle>Apple Inc.</securitiesClassTitle>
              <saleDate>05/06/2026</saleDate>
              <amountOfSecuritiesSold>250000</amountOfSecuritiesSold>
              <grossProceeds>71190164.00</grossProceeds>
            </securitiesSoldInPast3Months>
            <remarks>Shares sold in the THE HOMEPLACE TRUST U/A DTD 03/05/1999.</remarks>
          </formData>
        </edgarSubmission>
        </XML>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;

    // The exact ADR class title from Banco Santander's Form 144 (accession 0000950103-24-016159)
    // that overflowed the original 128-char SecurityClassTitle column.
    private const string LongAdrClassTitle =
        "American Depositary Shares, each representing the right to receive one Share of Capital Stock of Banco Santander, S.A., par value euro 0.50 each";

    private static readonly string LongAdrTitleSubmission = BuildSubmissionWithClassTitle(
        LongAdrClassTitle
    );

    // Minimal valid Form 144 ownership submission with a caller-supplied securities class title.
    private static string BuildSubmissionWithClassTitle(string classTitle) =>
        $"""
            <XML>
            <edgarSubmission xmlns="http://www.sec.gov/edgar/ownership">
              <formData>
                <issuerInfo>
                  <nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>Mahesh Chatta Aditya</nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>
                  <relationshipsToIssuer>
                    <relationshipToIssuer>Chief Risk Officer</relationshipToIssuer>
                  </relationshipsToIssuer>
                </issuerInfo>
                <securitiesInformation>
                  <securitiesClassTitle>{classTitle}</securitiesClassTitle>
                  <noOfUnitsSold>10665</noOfUnitsSold>
                  <aggregateMarketValue>50658.75</aggregateMarketValue>
                  <approxSaleDate>11/08/2024</approxSaleDate>
                  <securitiesExchangeName>NYSE</securitiesExchangeName>
                </securitiesInformation>
              </formData>
            </edgarSubmission>
            </XML>
            """;

    private const string MultiRelationshipSubmission = """
        <XML>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ownership">
          <formData>
            <issuerInfo>
              <nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>JANE DOE</nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>
              <relationshipsToIssuer>
                <relationshipToIssuer>Director</relationshipToIssuer>
                <relationshipToIssuer>Officer</relationshipToIssuer>
              </relationshipsToIssuer>
            </issuerInfo>
            <securitiesInformation>
              <securitiesClassTitle>Common</securitiesClassTitle>
              <noOfUnitsSold>100</noOfUnitsSold>
              <aggregateMarketValue>1000.00</aggregateMarketValue>
              <approxSaleDate>01/02/2026</approxSaleDate>
            </securitiesInformation>
          </formData>
        </edgarSubmission>
        </XML>
        """;

    private const string NothingToReportSubmission = """
        <XML>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ownership">
          <formData>
            <issuerInfo>
              <nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>JOHN ROE</nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>
              <relationshipsToIssuer>
                <relationshipToIssuer>Officer</relationshipToIssuer>
              </relationshipsToIssuer>
            </issuerInfo>
            <securitiesInformation>
              <securitiesClassTitle>Common</securitiesClassTitle>
              <noOfUnitsSold>500</noOfUnitsSold>
              <aggregateMarketValue>5000.00</aggregateMarketValue>
              <approxSaleDate>02/03/2026</approxSaleDate>
            </securitiesInformation>
            <nothingToReportFlagOnSecuritiesSoldInPast3Months>Y</nothingToReportFlagOnSecuritiesSoldInPast3Months>
            <securitiesSoldInPast3Months>
              <sellerDetails>
                <name>JOHN ROE</name>
              </sellerDetails>
            </securitiesSoldInPast3Months>
          </formData>
        </edgarSubmission>
        </XML>
        """;
}

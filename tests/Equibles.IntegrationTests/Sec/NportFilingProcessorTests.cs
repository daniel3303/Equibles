using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class NportFilingProcessorTests
{
    // ── CanProcess ──

    [Theory]
    [InlineData("NportP")]
    [InlineData("NportPa")]
    public void CanProcess_NportTypes_ReturnsTrue(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeTrue();
    }

    [Theory]
    [InlineData("NCen")]
    [InlineData("FormD")]
    [InlineData("TenK")]
    public void CanProcess_OtherTypes_ReturnsFalse(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeFalse();
    }

    // ── ParseDecimal ──

    [Theory]
    [InlineData("287467294.33", 287467294.33)]
    [InlineData("-477163.89", -477163.89)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("not-a-number", 0)]
    public void ParseDecimal_ParsesInvariantOrFallsBackToZero(string input, decimal expected)
    {
        NportFilingProcessor.ParseDecimal(input).Should().Be(expected);
    }

    // ── ParseDate ──

    [Fact]
    public void ParseDate_IsoFormat_Parses()
    {
        NportFilingProcessor.ParseDate("2026-02-28").Should().Be(new DateOnly(2026, 2, 28));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("02/28/2026")]
    public void ParseDate_InvalidOrEmpty_ReturnsNull(string input)
    {
        NportFilingProcessor.ParseDate(input).Should().BeNull();
    }

    // ── SanitizeXml ──

    [Fact]
    public void SanitizeXml_WithSgmlEnvelope_ExtractsInnerXml()
    {
        var result = NportFilingProcessor.SanitizeXml(ValidNportSubmission);
        result.Should().StartWith("<?xml").And.Contain("<edgarSubmission");
        result.Should().NotContain("<SEC-DOCUMENT>");
    }

    // ── Process ──

    [Fact]
    public async Task Process_ValidNport_InsertsFilingWithHoldings()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidNportSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().Include(f => f.Holdings).SingleAsync();
        filing.RegistrantName.Should().Be("ETF Opportunities Trust");
        filing.SeriesName.Should().Be("Big Tech Index ETF");
        filing.SeriesId.Should().Be("S000087771");
        filing.ReportPeriodDate.Should().Be(new DateOnly(2026, 2, 28));
        filing.ReportPeriodEnd.Should().Be(new DateOnly(2026, 8, 31));
        filing.TotalAssets.Should().Be(287467294.33m);
        filing.TotalLiabilities.Should().Be(193875501.04m);
        filing.NetAssets.Should().Be(93591793.29m);
        filing.IsFinalFiling.Should().BeFalse();
        filing.IsAmendment.Should().BeFalse();
        filing.ParserVersion.Should().Be(NportFiling.CurrentParserVersion);

        filing.Holdings.Should().HaveCount(2);

        var equity = filing.Holdings.Single(h => h.Name == "AT&T Inc");
        equity.Cusip.Should().Be("00206R102");
        equity.Isin.Should().Be("US00206R1023");
        equity.Balance.Should().Be(112500m);
        equity.Units.Should().Be("NS");
        equity.Currency.Should().Be("USD");
        equity.ValueUsd.Should().Be(1794375.00m);
        equity.PercentValue.Should().Be(1.92m);
        equity.PayoffProfile.Should().Be("Long");
        equity.AssetCategory.Should().Be("EC");
        equity.IssuerCategory.Should().Be("CORP");
        equity.InvestmentCountry.Should().Be("US");

        // The swap line reports its issuer category as the issuerConditional@issuerCat attribute.
        var swap = filing.Holdings.Single(h => h.Name == "STRATEGY INC");
        swap.IssuerCategory.Should().Be("OTHER");
        swap.AssetCategory.Should().Be("DE");
        swap.Cusip.Should().BeNull("the literal \"N/A\" CUSIP is stored as null");
        swap.ValueUsd.Should().Be(-477163.89m);
    }

    [Fact]
    public async Task Process_Amendment_SetsIsAmendment()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentSubmission);

        var result = await processor.Process(MakeFiling(form: "NPORT-P/A"), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.IsAmendment.Should().BeTrue();
    }

    [Fact]
    public async Task Process_DuplicateAccession_IsNotInsertedTwice()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidNportSubmission);
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
        NportFilingProcessor processor,
        NportFilingRepository repo,
        ISecEdgarClient secClient
    ) CreateProcessorWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var repo = new NportFilingRepository(dbContext);
        var errorRepo = new ErrorRepository(dbContext);
        var errorManager = new ErrorManager(errorRepo);
        var secClient = Substitute.For<ISecEdgarClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(NportFilingRepository), repo),
            (typeof(ErrorManager), errorManager)
        );

        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var processor = new NportFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<NportFilingProcessor>>(),
            errorReporter
        );

        return (processor, repo, secClient);
    }

    private static FilingData MakeFiling(string accession = null, string form = "NPORT-P")
    {
        accession ??= $"0001104659-26-{Guid.NewGuid().ToString("N")[..6]}";
        return new FilingData
        {
            AccessionNumber = accession,
            Form = form,
            FilingDate = new DateOnly(2026, 3, 30),
            ReportDate = new DateOnly(2026, 2, 28),
            Cik = "0001771146",
        };
    }

    private static CommonStock MakeCompany()
    {
        return new CommonStock
        {
            Ticker = "BTEC",
            Name = "Big Tech Index ETF",
            Cik = "0001771146",
        };
    }

    // A trimmed NPORT-P submission modelled on a real ETF filing: the SGML envelope plus the
    // <XML> body the processor reads. Carries one equity holding (issuerCat element) and one
    // total-return-swap holding (issuerConditional@issuerCat attribute, "N/A" CUSIP) to exercise
    // both issuer-category shapes and the "N/A" placeholder.
    private const string ValidNportSubmission = """
        <SEC-DOCUMENT>0001104659-26-000002.txt : 20260330
        <SEC-HEADER>...
        <DOCUMENT>
        <TYPE>NPORT-P
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/nport" xmlns:com="http://www.sec.gov/edgar/common" xmlns:ncom="http://www.sec.gov/edgar/nportcommon">
          <headerData>
            <submissionType>NPORT-P</submissionType>
            <isConfidential>false</isConfidential>
            <filerInfo>
              <filer>
                <issuerCredentials>
                  <cik>0001771146</cik>
                </issuerCredentials>
              </filer>
            </filerInfo>
          </headerData>
          <formData>
            <genInfo>
              <regName>ETF Opportunities Trust</regName>
              <regCik>0001771146</regCik>
              <regLei>549300FWST5041130Z58</regLei>
              <seriesName>Big Tech Index ETF</seriesName>
              <seriesId>S000087771</seriesId>
              <seriesLei>254900FIJG81260G5N49</seriesLei>
              <repPdEnd>2026-08-31</repPdEnd>
              <repPdDate>2026-02-28</repPdDate>
              <isFinalFiling>N</isFinalFiling>
            </genInfo>
            <fundInfo>
              <totAssets>287467294.33</totAssets>
              <totLiabs>193875501.04</totLiabs>
              <netAssets>93591793.29</netAssets>
            </fundInfo>
            <invstOrSecs>
                <invstOrSec>
                  <name>AT&amp;T Inc</name>
                  <lei>549300Z40J86GGSTL398</lei>
                  <title>AT&amp;T Inc</title>
                  <cusip>00206R102</cusip>
                  <identifiers>
                    <isin value="US00206R1023"/>
                    <ticker value="T"/>
                  </identifiers>
                  <balance>112500.00000000</balance>
                  <units>NS</units>
                  <curCd>USD</curCd>
                  <valUSD>1794375.00000000</valUSD>
                  <pctVal>1.92000000</pctVal>
                  <payoffProfile>Long</payoffProfile>
                  <assetCat>EC</assetCat>
                  <issuerCat>CORP</issuerCat>
                  <invCountry>US</invCountry>
                </invstOrSec>
                <invstOrSec>
                  <name>STRATEGY INC</name>
                  <lei>549300WQTWEJUEHXQX21</lei>
                  <title>RECV STRG TRS MSTR EQ</title>
                  <cusip>N/A</cusip>
                  <identifiers>
                    <isin value="US5949724083"/>
                  </identifiers>
                  <balance>68370086.61000000</balance>
                  <units>NC</units>
                  <curCd>USD</curCd>
                  <valUSD>-477163.89000000</valUSD>
                  <pctVal>-0.50983518236</pctVal>
                  <payoffProfile>Long</payoffProfile>
                  <assetCat>DE</assetCat>
                  <issuerConditional desc="Total Return Swap" issuerCat="OTHER"/>
                  <invCountry>US</invCountry>
                </invstOrSec>
            </invstOrSecs>
          </formData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;

    private const string AmendmentSubmission = """
        <SEC-DOCUMENT>0001104659-26-000003.txt : 20260330
        <DOCUMENT>
        <TYPE>NPORT-P/A
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/nport">
          <headerData>
            <submissionType>NPORT-P/A</submissionType>
            <filerInfo>
              <filer>
                <issuerCredentials>
                  <cik>0001771146</cik>
                </issuerCredentials>
              </filer>
            </filerInfo>
          </headerData>
          <formData>
            <genInfo>
              <regName>ETF Opportunities Trust</regName>
              <seriesName>Big Tech Index ETF</seriesName>
              <seriesId>S000087771</seriesId>
              <repPdEnd>2026-08-31</repPdEnd>
              <repPdDate>2026-02-28</repPdDate>
              <isFinalFiling>N</isFinalFiling>
            </genInfo>
            <fundInfo>
              <totAssets>287467294.33</totAssets>
              <totLiabs>193875501.04</totLiabs>
              <netAssets>93591793.29</netAssets>
            </fundInfo>
            <invstOrSecs/>
          </formData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;
}

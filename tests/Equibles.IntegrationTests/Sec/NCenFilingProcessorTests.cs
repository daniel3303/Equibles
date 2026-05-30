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

public class NCenFilingProcessorTests
{
    // ── CanProcess ──

    [Theory]
    [InlineData("NCen")]
    [InlineData("NCenA")]
    public void CanProcess_NCenTypes_ReturnsTrue(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeTrue();
    }

    [Theory]
    [InlineData("FormD")]
    [InlineData("Form144")]
    [InlineData("TenK")]
    public void CanProcess_OtherTypes_ReturnsFalse(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeFalse();
    }

    // ── ParseYesNo ──

    [Theory]
    [InlineData("Y", true)]
    [InlineData("y", true)]
    [InlineData("N", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParseYesNo_MapsYToTrue(string input, bool expected)
    {
        NCenFilingProcessor.ParseYesNo(input).Should().Be(expected);
    }

    // ── ParseDate ──

    [Fact]
    public void ParseDate_IsoFormat_Parses()
    {
        NCenFilingProcessor.ParseDate("2024-10-31").Should().Be(new DateOnly(2024, 10, 31));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10/31/2024")]
    public void ParseDate_InvalidOrEmpty_ReturnsNull(string input)
    {
        NCenFilingProcessor.ParseDate(input).Should().BeNull();
    }

    // ── SanitizeXml ──

    [Fact]
    public void SanitizeXml_WithSgmlEnvelope_ExtractsInnerXml()
    {
        var result = NCenFilingProcessor.SanitizeXml(ValidNCenSubmission);
        result.Should().StartWith("<?xml").And.Contain("<edgarSubmission");
        result.Should().NotContain("<SEC-DOCUMENT>");
    }

    // ── Process ──

    [Fact]
    public async Task Process_ValidNCen_InsertsFilingWithServiceProviders()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidNCenSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().Include(f => f.ServiceProviders).SingleAsync();
        filing.RegistrantName.Should().Be("MEXICO FUND INC");
        filing.InvestmentCompanyType.Should().Be("N-2");
        filing.InvestmentCompanyFileNumber.Should().Be("811-02409");
        filing.RegistrantLei.Should().Be("00000000000000238096");
        filing.State.Should().Be("US-MD");
        filing.Country.Should().Be("US");
        filing.ReportEndingPeriod.Should().Be(new DateOnly(2024, 10, 31));
        filing.IsReportPeriodLessThan12Months.Should().BeFalse();
        filing.IsFirstFiling.Should().BeFalse();
        filing.IsLastFiling.Should().BeFalse();
        filing.IsFamilyInvestmentCompany.Should().BeFalse();
        filing.IsAmendment.Should().BeFalse();

        filing
            .ServiceProviders.Should()
            .Contain(p =>
                p.ProviderType == NCenServiceProviderType.InvestmentAdviser
                && p.Name == "IMPULSORA DEL FONDO MEXICO SC"
                && p.Country == "MX"
            );
        filing
            .ServiceProviders.Should()
            .Contain(p =>
                p.ProviderType == NCenServiceProviderType.Custodian && p.Name == "BBVA MEXICO SA"
            );
        filing
            .ServiceProviders.Should()
            .Contain(p =>
                p.ProviderType == NCenServiceProviderType.TransferAgent
                && p.Name == "EQUINITI TRUST COMPANY LLC"
                && p.Country == "US"
            );
        filing
            .ServiceProviders.Should()
            .Contain(p =>
                p.ProviderType == NCenServiceProviderType.Administrator
                && p.Name == "IFM CAPITAL LLC"
                && p.IsAffiliated
            );
        filing
            .ServiceProviders.Should()
            .Contain(p =>
                p.ProviderType == NCenServiceProviderType.PublicAccountant
                && p.Name == "TAIT, WELLER & BAKER LLP"
            );
        // "N/A" placeholder providers (sub-adviser, pricing service, underwriter) are skipped.
        filing
            .ServiceProviders.Should()
            .NotContain(p => p.ProviderType == NCenServiceProviderType.SubAdviser);
        filing
            .ServiceProviders.Should()
            .NotContain(p => p.ProviderType == NCenServiceProviderType.PrincipalUnderwriter);
    }

    [Fact]
    public async Task Process_MultiSeries_DeDuplicatesRepeatedProviders()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(MultiSeriesSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().Include(f => f.ServiceProviders).SingleAsync();
        filing
            .ServiceProviders.Count(p =>
                p.ProviderType == NCenServiceProviderType.InvestmentAdviser
                && p.Name == "VANGUARD GROUP INC"
            )
            .Should()
            .Be(1, "the same adviser named on multiple series collapses to one row");
        filing.ServiceProviders.Should().Contain(p => p.Name == "STATE STREET BANK AND TRUST");
    }

    [Fact]
    public async Task Process_Amendment_SetsIsAmendment()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentSubmission);

        var result = await processor.Process(MakeFiling(form: "N-CEN/A"), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.IsAmendment.Should().BeTrue();
    }

    [Fact]
    public async Task Process_DuplicateAccession_IsNotInsertedTwice()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidNCenSubmission);
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
        NCenFilingProcessor processor,
        NCenFilingRepository repo,
        ISecEdgarClient secClient
    ) CreateProcessorWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var repo = new NCenFilingRepository(dbContext);
        var errorRepo = new ErrorRepository(dbContext);
        var errorManager = new ErrorManager(errorRepo);
        var secClient = Substitute.For<ISecEdgarClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(NCenFilingRepository), repo),
            (typeof(ErrorManager), errorManager)
        );

        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var processor = new NCenFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<NCenFilingProcessor>>(),
            errorReporter
        );

        return (processor, repo, secClient);
    }

    private static FilingData MakeFiling(string accession = null, string form = "N-CEN")
    {
        accession ??= $"0000065433-24-{Guid.NewGuid().ToString("N")[..6]}";
        return new FilingData
        {
            AccessionNumber = accession,
            Form = form,
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 10, 31),
            Cik = "0000065433",
        };
    }

    private static CommonStock MakeCompany()
    {
        return new CommonStock
        {
            Ticker = "MXF",
            Name = "Mexico Fund Inc",
            Cik = "0000065433",
        };
    }

    // A real N-CEN submission (Mexico Fund Inc, accession 0000065433-24-000002), trimmed to the
    // SGML envelope plus the <XML> body the processor reads. Includes "N/A" placeholder providers
    // (sub-adviser, pricing service, underwriter) to verify they are filtered out.
    private const string ValidNCenSubmission = """
        <SEC-DOCUMENT>0000065433-24-000002.txt : 20250115
        <SEC-HEADER>...
        <DOCUMENT>
        <TYPE>N-CEN
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ncen" xmlns:com="http://www.sec.gov/edgar/common">
          <headerData>
            <submissionType>N-CEN</submissionType>
            <filerInfo>
              <investmentCompanyType>N-2</investmentCompanyType>
            </filerInfo>
          </headerData>
          <formData>
            <generalInfo isReportPeriodLt12="N" reportEndingPeriod="2024-10-31"/>
            <registrantInfo>
              <registrantFullName>MEXICO FUND INC</registrantFullName>
              <investmentCompFileNo>811-02409</investmentCompFileNo>
              <registrantLei>00000000000000238096</registrantLei>
              <registrantstate>US-MD</registrantstate>
              <registrantcountry>US</registrantcountry>
              <isRegistrantFirstFiling>N</isRegistrantFirstFiling>
              <isRegistrantLastFiling>N</isRegistrantLastFiling>
              <isRegistrantFamilyInvComp>N</isRegistrantFamilyInvComp>
              <principalUnderwriters>
                <principalUnderwriter>
                  <principalUnderwriterName>N/A</principalUnderwriterName>
                  <principalUnderWriterCountry>MX</principalUnderWriterCountry>
                  <isPrincipalUnderwriterAffiliatedWithRegistrant>N</isPrincipalUnderwriterAffiliatedWithRegistrant>
                </principalUnderwriter>
              </principalUnderwriters>
              <publicAccountants>
                <publicAccountant>
                  <publicAccountantName>TAIT, WELLER &amp; BAKER LLP</publicAccountantName>
                  <publicAccountantStateCountry publicAccountantState="US-PA" publicAccountantCountry="US"/>
                </publicAccountant>
              </publicAccountants>
            </registrantInfo>
            <managementInvestmentQuestionSeriesInfo>
              <managementInvestmentQuestion>
                <mgmtInvFundName>THE MEXICO FUND INC</mgmtInvFundName>
                <investmentAdvisers>
                  <investmentAdviser>
                    <investmentAdviserName>IMPULSORA DEL FONDO MEXICO SC</investmentAdviserName>
                    <investmentAdviserCountry>MX</investmentAdviserCountry>
                  </investmentAdviser>
                </investmentAdvisers>
                <subAdvisers>
                  <subAdviser>
                    <subAdviserName>N/A</subAdviserName>
                    <subAdviserCountry>MX</subAdviserCountry>
                    <isSubAdviserAffiliated>N</isSubAdviserAffiliated>
                  </subAdviser>
                </subAdvisers>
                <transferAgents>
                  <transferAgent>
                    <transferAgentName>EQUINITI TRUST COMPANY LLC</transferAgentName>
                    <transferAgentStateCountry transferAgentState="US-NY" transferAgentCountry="US"/>
                    <isTransferAgentAffiliated>N</isTransferAgentAffiliated>
                  </transferAgent>
                </transferAgents>
                <pricingServices>
                  <pricingService>
                    <pricingServiceName>N/A</pricingServiceName>
                    <pricingServiceCountry>MX</pricingServiceCountry>
                    <isPricingServiceAffiliated>N</isPricingServiceAffiliated>
                  </pricingService>
                </pricingServices>
                <custodians>
                  <custodian>
                    <custodianName>BBVA MEXICO SA</custodianName>
                    <custodianCountry>MX</custodianCountry>
                    <isCustodianAffiliated>N</isCustodianAffiliated>
                  </custodian>
                </custodians>
                <admins>
                  <admin>
                    <adminName>IFM CAPITAL LLC</adminName>
                    <adminStateCountry adminState="US-UT" adminCountry="US"/>
                    <isAdminAffiliated>Y</isAdminAffiliated>
                  </admin>
                </admins>
              </managementInvestmentQuestion>
            </managementInvestmentQuestionSeriesInfo>
          </formData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;

    // Two series naming the same adviser, to verify provider de-duplication.
    private const string MultiSeriesSubmission = """
        <XML>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ncen">
          <headerData>
            <submissionType>N-CEN</submissionType>
            <filerInfo>
              <investmentCompanyType>N-1A</investmentCompanyType>
            </filerInfo>
          </headerData>
          <formData>
            <generalInfo isReportPeriodLt12="N" reportEndingPeriod="2024-12-31"/>
            <registrantInfo>
              <registrantFullName>VANGUARD INDEX FUNDS</registrantFullName>
              <isRegistrantFirstFiling>N</isRegistrantFirstFiling>
            </registrantInfo>
            <managementInvestmentQuestionSeriesInfo>
              <managementInvestmentQuestion>
                <investmentAdvisers>
                  <investmentAdviser>
                    <investmentAdviserName>VANGUARD GROUP INC</investmentAdviserName>
                    <investmentAdviserCountry>US</investmentAdviserCountry>
                  </investmentAdviser>
                </investmentAdvisers>
                <custodians>
                  <custodian>
                    <custodianName>STATE STREET BANK AND TRUST</custodianName>
                    <custodianCountry>US</custodianCountry>
                    <isCustodianAffiliated>N</isCustodianAffiliated>
                  </custodian>
                </custodians>
              </managementInvestmentQuestion>
              <managementInvestmentQuestion>
                <investmentAdvisers>
                  <investmentAdviser>
                    <investmentAdviserName>VANGUARD GROUP INC</investmentAdviserName>
                    <investmentAdviserCountry>US</investmentAdviserCountry>
                  </investmentAdviser>
                </investmentAdvisers>
              </managementInvestmentQuestion>
            </managementInvestmentQuestionSeriesInfo>
          </formData>
        </edgarSubmission>
        </XML>
        """;

    private const string AmendmentSubmission = """
        <XML>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/ncen">
          <headerData>
            <submissionType>N-CEN/A</submissionType>
            <filerInfo>
              <investmentCompanyType>N-2</investmentCompanyType>
            </filerInfo>
          </headerData>
          <formData>
            <generalInfo isReportPeriodLt12="N" reportEndingPeriod="2024-10-31"/>
            <registrantInfo>
              <registrantFullName>MEXICO FUND INC</registrantFullName>
            </registrantInfo>
          </formData>
        </edgarSubmission>
        </XML>
        """;
}

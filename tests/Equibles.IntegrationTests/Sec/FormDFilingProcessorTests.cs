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

public class FormDFilingProcessorTests
{
    // ── CanProcess ──

    [Theory]
    [InlineData("FormD")]
    [InlineData("FormDa")]
    public void CanProcess_FormDTypes_ReturnsTrue(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeTrue();
    }

    [Theory]
    [InlineData("FormFour")]
    [InlineData("Form144")]
    [InlineData("TenK")]
    public void CanProcess_OtherTypes_ReturnsFalse(string value)
    {
        var (processor, _, _) = CreateProcessorWithDeps();
        processor.CanProcess(DocumentType.FromValue(value)).Should().BeFalse();
    }

    // ── ParseAmount ──

    [Fact]
    public void ParseAmount_NumericValue_ReturnsAmountNotIndefinite()
    {
        FormDFilingProcessor.ParseAmount("5000000").Should().Be((5000000L, false));
    }

    [Fact]
    public void ParseAmount_Indefinite_ReturnsNullFlagged()
    {
        FormDFilingProcessor.ParseAmount("Indefinite").Should().Be(((long?)null, true));
        FormDFilingProcessor.ParseAmount("indefinite").Should().Be(((long?)null, true));
    }

    [Fact]
    public void ParseAmount_EmptyOrNull_ReturnsNullNotIndefinite()
    {
        FormDFilingProcessor.ParseAmount("").Should().Be(((long?)null, false));
        FormDFilingProcessor.ParseAmount(null).Should().Be(((long?)null, false));
    }

    // ── ParseDate ──

    [Theory]
    [InlineData("2025-02-28", 2025, 2, 28)]
    [InlineData("02/28/2025", 2025, 2, 28)]
    public void ParseDate_IsoOrUsFormat_Parses(string input, int y, int m, int d)
    {
        FormDFilingProcessor.ParseDate(input).Should().Be(new DateOnly(y, m, d));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void ParseDate_InvalidOrEmpty_ReturnsNull(string input)
    {
        FormDFilingProcessor.ParseDate(input).Should().BeNull();
    }

    // ── SanitizeXml ──

    [Fact]
    public void SanitizeXml_WithSgmlEnvelope_ExtractsInnerXml()
    {
        var result = FormDFilingProcessor.SanitizeXml(ValidFormDSubmission);
        result.Should().StartWith("<?xml").And.Contain("<edgarSubmission");
        result.Should().NotContain("<SEC-DOCUMENT>");
    }

    // ── Process ──

    [Fact]
    public async Task Process_ValidFormD_InsertsFilingWithRelatedPersons()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidFormDSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().Include(f => f.RelatedPersons).SingleAsync();
        filing.EntityName.Should().Be("AJ BOULDER FUND LLC");
        filing.EntityType.Should().Be("Limited Liability Company");
        filing.JurisdictionOfInc.Should().Be("DELAWARE");
        filing.YearOfIncorporation.Should().Be(2024);
        filing.IndustryGroup.Should().Be("Pooled Investment Fund");
        filing.FederalExemptions.Should().Be("06b, 3C, 3C.7");
        filing.IsAmendment.Should().BeFalse();
        filing.TotalOfferingAmount.Should().BeNull();
        filing.IsOfferingAmountIndefinite.Should().BeTrue();
        filing.TotalAmountSold.Should().Be(0);
        filing.TotalRemaining.Should().BeNull();
        filing.IsRemainingIndefinite.Should().BeTrue();
        filing.MinimumInvestmentAccepted.Should().Be(0);
        filing.HasNonAccreditedInvestors.Should().BeFalse();
        filing.TotalNumberAlreadyInvested.Should().Be(0);

        filing.RelatedPersons.Should().HaveCount(2);
        filing.RelatedPersons.Should().Contain(p => p.Name == "BENJAMIN WEPRIN");
        filing
            .RelatedPersons.Should()
            .OnlyContain(p => p.Relationships == "Executive Officer, Promoter");
    }

    [Fact]
    public async Task Process_NumericAmounts_ParsesDollarFiguresAndDate()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(NumericAmountsSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.TotalOfferingAmount.Should().Be(10000000);
        filing.IsOfferingAmountIndefinite.Should().BeFalse();
        filing.TotalAmountSold.Should().Be(2500000);
        filing.TotalRemaining.Should().Be(7500000);
        filing.IsRemainingIndefinite.Should().BeFalse();
        filing.MinimumInvestmentAccepted.Should().Be(25000);
        filing.HasNonAccreditedInvestors.Should().BeTrue();
        filing.TotalNumberAlreadyInvested.Should().Be(12);
        filing.DateOfFirstSale.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public async Task Process_AmendmentFlag_SetsIsAmendment()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(AmendmentSubmission);

        var result = await processor.Process(MakeFiling(form: "D/A"), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.IsAmendment.Should().BeTrue();
    }

    [Fact]
    public async Task Process_DuplicateAccession_IsNotInsertedTwice()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient.GetDocumentContent(Arg.Any<FilingData>()).Returns(ValidFormDSubmission);
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
        FormDFilingProcessor processor,
        FormDFilingRepository repo,
        ISecEdgarClient secClient
    ) CreateProcessorWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var repo = new FormDFilingRepository(dbContext);
        var errorRepo = new ErrorRepository(dbContext);
        var errorManager = new ErrorManager(errorRepo);
        var secClient = Substitute.For<ISecEdgarClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ISecEdgarClient), secClient),
            (typeof(FormDFilingRepository), repo),
            (typeof(ErrorManager), errorManager)
        );

        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var processor = new FormDFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<FormDFilingProcessor>>(),
            errorReporter
        );

        return (processor, repo, secClient);
    }

    private static FilingData MakeFiling(string accession = null, string form = "D")
    {
        accession ??= $"0002058722-25-{Guid.NewGuid().ToString("N")[..6]}";
        return new FilingData
        {
            AccessionNumber = accession,
            Form = form,
            FilingDate = new DateOnly(2025, 2, 28),
            ReportDate = new DateOnly(2025, 2, 28),
            Cik = "0002058722",
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

    // A real Form D submission (accession 0002058722-25-000001), trimmed to the SGML envelope
    // plus the <XML> body the processor reads.
    private const string ValidFormDSubmission = """
        <SEC-DOCUMENT>0002058722-25-000001.txt : 20250228
        <SEC-HEADER>...
        <DOCUMENT>
        <TYPE>D
        <TEXT>
        <XML>
        <?xml version="1.0"?>
        <edgarSubmission>
          <schemaVersion>X0708</schemaVersion>
          <submissionType>D</submissionType>
          <testOrLive>LIVE</testOrLive>
          <primaryIssuer>
            <cik>0002058722</cik>
            <entityName>AJ BOULDER FUND LLC</entityName>
            <jurisdictionOfInc>DELAWARE</jurisdictionOfInc>
            <entityType>Limited Liability Company</entityType>
            <yearOfInc>
              <withinFiveYears>true</withinFiveYears>
              <value>2024</value>
            </yearOfInc>
          </primaryIssuer>
          <relatedPersonsList>
            <relatedPersonInfo>
              <relatedPersonName>
                <firstName>BENJAMIN</firstName>
                <lastName>WEPRIN</lastName>
              </relatedPersonName>
              <relatedPersonRelationshipList>
                <relationship>Executive Officer</relationship>
                <relationship>Promoter</relationship>
              </relatedPersonRelationshipList>
            </relatedPersonInfo>
            <relatedPersonInfo>
              <relatedPersonName>
                <firstName>ERIC</firstName>
                <lastName>HASSBERGER</lastName>
              </relatedPersonName>
              <relatedPersonRelationshipList>
                <relationship>Executive Officer</relationship>
                <relationship>Promoter</relationship>
              </relatedPersonRelationshipList>
            </relatedPersonInfo>
          </relatedPersonsList>
          <offeringData>
            <industryGroup>
              <industryGroupType>Pooled Investment Fund</industryGroupType>
            </industryGroup>
            <federalExemptionsExclusions>
              <item>06b</item>
              <item>3C</item>
              <item>3C.7</item>
            </federalExemptionsExclusions>
            <typeOfFiling>
              <newOrAmendment>
                <isAmendment>false</isAmendment>
              </newOrAmendment>
              <dateOfFirstSale>
                <yetToOccur>true</yetToOccur>
              </dateOfFirstSale>
            </typeOfFiling>
            <minimumInvestmentAccepted>0</minimumInvestmentAccepted>
            <offeringSalesAmounts>
              <totalOfferingAmount>Indefinite</totalOfferingAmount>
              <totalAmountSold>0</totalAmountSold>
              <totalRemaining>Indefinite</totalRemaining>
            </offeringSalesAmounts>
            <investors>
              <hasNonAccreditedInvestors>false</hasNonAccreditedInvestors>
              <totalNumberAlreadyInvested>0</totalNumberAlreadyInvested>
            </investors>
          </offeringData>
        </edgarSubmission>
        </XML>
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;

    private const string NumericAmountsSubmission = """
        <XML>
        <edgarSubmission>
          <submissionType>D</submissionType>
          <primaryIssuer>
            <entityName>ACME ROBOTICS INC</entityName>
            <entityType>Corporation</entityType>
            <jurisdictionOfInc>DELAWARE</jurisdictionOfInc>
          </primaryIssuer>
          <offeringData>
            <industryGroup>
              <industryGroupType>Technology</industryGroupType>
            </industryGroup>
            <typeOfFiling>
              <newOrAmendment>
                <isAmendment>false</isAmendment>
              </newOrAmendment>
              <dateOfFirstSale>
                <value>2025-01-15</value>
              </dateOfFirstSale>
            </typeOfFiling>
            <minimumInvestmentAccepted>25000</minimumInvestmentAccepted>
            <offeringSalesAmounts>
              <totalOfferingAmount>10000000</totalOfferingAmount>
              <totalAmountSold>2500000</totalAmountSold>
              <totalRemaining>7500000</totalRemaining>
            </offeringSalesAmounts>
            <investors>
              <hasNonAccreditedInvestors>true</hasNonAccreditedInvestors>
              <totalNumberAlreadyInvested>12</totalNumberAlreadyInvested>
            </investors>
          </offeringData>
        </edgarSubmission>
        </XML>
        """;

    private const string AmendmentSubmission = """
        <XML>
        <edgarSubmission>
          <submissionType>D/A</submissionType>
          <primaryIssuer>
            <entityName>ACME ROBOTICS INC</entityName>
          </primaryIssuer>
          <offeringData>
            <typeOfFiling>
              <newOrAmendment>
                <isAmendment>true</isAmendment>
              </newOrAmendment>
            </typeOfFiling>
            <offeringSalesAmounts>
              <totalOfferingAmount>10000000</totalOfferingAmount>
              <totalAmountSold>5000000</totalAmountSold>
              <totalRemaining>5000000</totalRemaining>
            </offeringSalesAmounts>
          </offeringData>
        </edgarSubmission>
        </XML>
        """;
}

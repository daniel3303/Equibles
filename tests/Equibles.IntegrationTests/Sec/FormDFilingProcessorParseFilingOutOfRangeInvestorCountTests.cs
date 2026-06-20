using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class FormDFilingProcessorParseFilingOutOfRangeInvestorCountTests
{
    // Contract: TotalNumberAlreadyInvested is a count of investors, so it can never be
    // negative. The Form D XML is filer-entered, so an out-of-range value (a fat-fingered
    // count, or a figure pasted into the wrong field) must not silently corrupt the stored
    // row. ParseLong reads the long correctly (3,000,000,000 < Int64.Max); the bug is the
    // unchecked (int) narrowing at the call site, which wraps it to a negative count.
    [Fact]
    public async Task Process_InvestorCountAboveInt32Max_DoesNotStoreNegativeCount()
    {
        var (processor, repo, secClient) = CreateProcessorWithDeps();
        secClient
            .GetDocumentContent(Arg.Any<FilingData>())
            .Returns(OutOfRangeInvestorCountSubmission);

        var result = await processor.Process(MakeFiling(), MakeCompany());

        result.Should().BeTrue();
        var filing = await repo.GetAll().SingleAsync();
        filing.TotalNumberAlreadyInvested.Should().BeGreaterThanOrEqualTo(0);
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

    // totalNumberAlreadyInvested exceeds Int32.MaxValue (2,147,483,647). It parses cleanly
    // as a long, so the only thing standing between it and the int column is the cast.
    private const string OutOfRangeInvestorCountSubmission = """
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
            </typeOfFiling>
            <offeringSalesAmounts>
              <totalOfferingAmount>10000000</totalOfferingAmount>
              <totalAmountSold>2500000</totalAmountSold>
              <totalRemaining>7500000</totalRemaining>
            </offeringSalesAmounts>
            <investors>
              <hasNonAccreditedInvestors>true</hasNonAccreditedInvestors>
              <totalNumberAlreadyInvested>3000000000</totalNumberAlreadyInvested>
            </investors>
          </offeringData>
        </edgarSubmission>
        </XML>
        """;
}

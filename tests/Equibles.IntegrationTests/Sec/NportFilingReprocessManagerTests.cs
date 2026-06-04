using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class NportFilingReprocessManagerTests
{
    [Fact]
    public async Task Run_StaleFilingWithNoHoldings_FetchesReparsesAndStampsVersion()
    {
        var (manager, dbContext, secClient) = CreateManagerWithDeps();
        var stock = SeedStock(dbContext);
        // A filing imported before holdings were parsed correctly: version 0, zero holdings.
        var filing = SeedFiling(dbContext, stock, parserVersion: 0);
        secClient
            .GetDocumentContent(filing.AccessionNumber, Arg.Any<string>())
            .Returns(ValidNportSubmission);

        var result = await manager.Run();

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.HoldingsAdded.Should().Be(2);
        result.Failed.Should().Be(0);
        await secClient.Received().GetDocumentContent(filing.AccessionNumber, Arg.Any<string>());

        var reprocessed = await dbContext.Set<NportFiling>().Include(f => f.Holdings).SingleAsync();
        reprocessed.ParserVersion.Should().Be(NportFiling.CurrentParserVersion);
        reprocessed.Holdings.Should().HaveCount(2);
        reprocessed.Holdings.Should().Contain(h => h.Name == "AT&T Inc" && h.Cusip == "00206R102");
        reprocessed.NetAssets.Should().Be(93591793.29m);
    }

    [Fact]
    public async Task Run_FilingAlreadyAtCurrentVersion_IsLeftUntouched()
    {
        var (manager, dbContext, secClient) = CreateManagerWithDeps();
        var stock = SeedStock(dbContext);
        SeedFiling(dbContext, stock, parserVersion: NportFiling.CurrentParserVersion);

        var result = await manager.Run();

        result.Total.Should().Be(0);
        result.Processed.Should().Be(0);
        // A current-version filing is never re-fetched.
        await secClient.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Run_FilingWithEmptyHoldings_StampsVersionWithNoHoldings()
    {
        var (manager, dbContext, secClient) = CreateManagerWithDeps();
        var stock = SeedStock(dbContext);
        SeedFiling(dbContext, stock, parserVersion: 0);
        secClient
            .GetDocumentContent(Arg.Any<string>(), Arg.Any<string>())
            .Returns(EmptyHoldingsSubmission);

        var result = await manager.Run();

        result.Processed.Should().Be(1);
        result.HoldingsAdded.Should().Be(0);
        result.Failed.Should().Be(0);

        // A legitimately empty filing still advances, so it doesn't re-select itself forever.
        var reprocessed = await dbContext.Set<NportFiling>().Include(f => f.Holdings).SingleAsync();
        reprocessed.ParserVersion.Should().Be(NportFiling.CurrentParserVersion);
        reprocessed.Holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_FetchKeepsFailing_GivesUpAfterCeilingAndStopsReselecting()
    {
        var (manager, dbContext, secClient) = CreateManagerWithDeps();
        var stock = SeedStock(dbContext);
        SeedFiling(dbContext, stock, parserVersion: 0);
        // EDGAR returns empty content every time, so every reprocess attempt fails.
        secClient.GetDocumentContent(Arg.Any<string>(), Arg.Any<string>()).Returns("");

        // One failed attempt per run until the ceiling is reached.
        for (var attempt = 0; attempt < NportFilingReprocessManager.MaxReprocessAttempts; attempt++)
        {
            var failing = await manager.Run();
            failing.Failed.Should().Be(1);
        }

        var givenUp = await dbContext.Set<NportFiling>().Include(f => f.Holdings).SingleAsync();
        givenUp.ReprocessAttempts.Should().Be(NportFilingReprocessManager.MaxReprocessAttempts);
        givenUp.ParserVersion.Should().Be(NportFiling.CurrentParserVersion);
        givenUp.Holdings.Should().BeEmpty();

        // Once stamped it leaves the work-set and is never re-fetched again.
        secClient.ClearReceivedCalls();
        var afterGiveUp = await manager.Run();
        afterGiveUp.Total.Should().Be(0);
        await secClient.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());
    }

    // ── Helpers ──

    private static (
        NportFilingReprocessManager manager,
        Equibles.Data.EquiblesFinancialDbContext dbContext,
        ISecEdgarClient secClient
    ) CreateManagerWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var repo = new NportFilingRepository(dbContext);
        var secClient = Substitute.For<ISecEdgarClient>();

        // The error reporter is only reached when a submission fails to parse; the valid-XML paths
        // here never invoke it, so a scope factory carrying just the error manager is enough.
        var errorManager = new ErrorManager(new ErrorRepository(dbContext));
        var scopeFactory = ServiceScopeSubstitute.Create((typeof(ErrorManager), errorManager));
        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var manager = new NportFilingReprocessManager(
            repo,
            secClient,
            dbContext,
            errorReporter,
            Substitute.For<ILogger<NportFilingReprocessManager>>()
        );

        return (manager, dbContext, secClient);
    }

    private static CommonStock SeedStock(Equibles.Data.EquiblesFinancialDbContext dbContext)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BTEC",
            Name = "Big Tech Index ETF",
            Cik = "0001771146",
        };
        dbContext.Add(stock);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
        return stock;
    }

    private static NportFiling SeedFiling(
        Equibles.Data.EquiblesFinancialDbContext dbContext,
        CommonStock stock,
        int parserVersion
    )
    {
        var filing = new NportFiling
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            AccessionNumber = "0001104659-26-000099",
            FilingDate = new DateOnly(2026, 3, 30),
            ReportPeriodDate = new DateOnly(2026, 2, 28),
            ReportPeriodEnd = new DateOnly(2026, 2, 28),
            ParserVersion = parserVersion,
        };
        dbContext.Add(filing);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
        return filing;
    }

    // A trimmed NPORT-P submission with two holdings, laid out as real EDGAR filings are —
    // <invstOrSecs> is a child of <formData>, a sibling of <fundInfo>.
    private const string ValidNportSubmission = """
        <SEC-DOCUMENT>0001104659-26-000099.txt : 20260330
        <DOCUMENT>
        <TYPE>NPORT-P
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/nport">
          <headerData>
            <submissionType>NPORT-P</submissionType>
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
            <invstOrSecs>
              <invstOrSec>
                <name>AT&amp;T Inc</name>
                <cusip>00206R102</cusip>
                <balance>112500.00000000</balance>
                <units>NS</units>
                <curCd>USD</curCd>
                <valUSD>1794375.00000000</valUSD>
                <pctVal>1.92000000</pctVal>
                <assetCat>EC</assetCat>
                <issuerCat>CORP</issuerCat>
                <invCountry>US</invCountry>
              </invstOrSec>
              <invstOrSec>
                <name>Microsoft Corp</name>
                <cusip>594918104</cusip>
                <balance>5000.00000000</balance>
                <units>NS</units>
                <curCd>USD</curCd>
                <valUSD>2100000.00000000</valUSD>
                <pctVal>2.24000000</pctVal>
                <assetCat>EC</assetCat>
                <issuerCat>CORP</issuerCat>
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

    // A valid NPORT-P submission that reports no portfolio investments (e.g. a final filing).
    private const string EmptyHoldingsSubmission = """
        <SEC-DOCUMENT>0001104659-26-000099.txt : 20260330
        <DOCUMENT>
        <TYPE>NPORT-P
        <TEXT>
        <XML>
        <?xml version="1.0" encoding="UTF-8"?>
        <edgarSubmission xmlns="http://www.sec.gov/edgar/nport">
          <headerData>
            <submissionType>NPORT-P</submissionType>
          </headerData>
          <formData>
            <genInfo>
              <regName>ETF Opportunities Trust</regName>
              <repPdEnd>2026-08-31</repPdEnd>
              <repPdDate>2026-02-28</repPdDate>
            </genInfo>
            <fundInfo>
              <netAssets>0.00</netAssets>
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

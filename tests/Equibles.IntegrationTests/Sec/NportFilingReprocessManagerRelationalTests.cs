using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the reprocess manager's RELATIONAL replace branch — the transaction + set-based
/// ExecuteDelete that production runs — against real Postgres with lazy-loading proxies enabled,
/// exactly the configuration whose Include/lazy-load of a six-figure schedule once OOM-killed the
/// worker. The in-memory suite can only ever exercise the tracker-based fallback.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class NportFilingReprocessManagerRelationalTests : ParadeDbMcpTestBase
{
    public NportFilingReprocessManagerRelationalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_FilingWithExistingHoldings_ReplacesScheduleThroughTheRelationalBranch()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BTEC",
            Name = "Big Tech Index ETF",
            Cik = "0001771146",
        };
        var filing = new NportFiling
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            AccessionNumber = "0001104659-26-000099",
            FilingDate = new DateOnly(2026, 3, 30),
            ReportPeriodDate = new DateOnly(2026, 2, 28),
            ReportPeriodEnd = new DateOnly(2026, 2, 28),
            ParserVersion = 1,
        };
        DbContext.Add(stock);
        DbContext.Add(filing);
        // A schedule left by the previous parser version — the relational branch must delete it
        // set-based and land the reparsed rows in the same committed transaction.
        DbContext.Add(
            new NportHolding
            {
                Id = Guid.NewGuid(),
                NportFilingId = filing.Id,
                Name = "Stale Holding Corp",
                Cusip = "STALE0001",
                Balance = 1m,
                ValueUsd = 1m,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runCtx = Fixture.CreateDbContext();
        var secClient = Substitute.For<ISecEdgarClient>();
        secClient
            .GetDocumentContent(filing.AccessionNumber, Arg.Any<string>())
            .Returns(ValidNportSubmission);
        var errorReporter = new ErrorReporter(
            ServiceScopeSubstitute.Create(
                (typeof(ErrorManager), new ErrorManager(new ErrorRepository(runCtx)))
            ),
            NullLogger<ErrorReporter>()
        );
        var manager = new NportFilingReprocessManager(
            new NportFilingRepository(runCtx),
            new CommonStockRepository(runCtx),
            secClient,
            runCtx,
            errorReporter,
            NullLogger<NportFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Processed.Should().Be(1);
        result.HoldingsAdded.Should().Be(2);
        result.Failed.Should().Be(0);

        var reprocessed = await DbContext.Set<NportFiling>().Include(f => f.Holdings).SingleAsync();
        reprocessed.ParserVersion.Should().Be(NportFiling.CurrentParserVersion);
        reprocessed.Holdings.Should().HaveCount(2);
        reprocessed.Holdings.Should().NotContain(h => h.Name == "Stale Holding Corp");
        reprocessed.Holdings.Should().Contain(h => h.Name == "Microsoft Corp");
        reprocessed.ReportedHoldingCount.Should().Be(2);
    }

    // A trimmed NPORT-P submission with two holdings, laid out as real EDGAR filings are.
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
}

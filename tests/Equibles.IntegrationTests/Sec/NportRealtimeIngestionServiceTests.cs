using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
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

/// <summary>
/// The daily-index NPORT-P sweep: it must surface fund-family trusts that are not tracked stocks
/// (the held-by-funds coverage gap), keep only their positions in stocks we track, leave tracked-
/// stock registrants to the issuer-feed crawler, and never re-download a filing it has already
/// handled.
/// </summary>
public class NportRealtimeIngestionServiceTests
{
    private const string AppleCusip = "037833100";
    private const string BondCusip = "912828YK0";

    [Fact]
    public async Task IngestRecentFilings_TrustHoldingTrackedStock_StoresFilteredToTrackedHoldings()
    {
        var (service, dbContext, secClient) = CreateServiceWithDeps();
        SeedStock(dbContext, "AAPL", cik: "0000320193", cusip: AppleCusip);

        var entry = Entry("0000036405-26-000001", cik: "36405");
        StubDailyIndex(secClient, entry);
        StubContent(
            secClient,
            entry,
            NportXml(
                "VANGUARD INDEX FUNDS",
                "S000002277",
                (AppleCusip, "APPLE INC", 170_000_000m),
                (BondCusip, "US TREASURY NOTE", 5_000_000m)
            )
        );

        var result = await service.IngestRecentFilings(
            new DateOnly(2026, 3, 1),
            lookbackDays: 1,
            maxFetchesPerCycle: 100,
            CancellationToken.None
        );

        result.Stored.Should().Be(1);

        var stored = await dbContext.Set<NportFiling>().Include(f => f.Holdings).SingleAsync();
        stored.CommonStockId.Should().BeNull();
        stored.RegistrantCik.Should().Be("36405");
        stored.RegistrantName.Should().Be("VANGUARD INDEX FUNDS");
        // The bond is dropped; only the tracked-stock position is kept.
        stored.Holdings.Should().ContainSingle().Which.Cusip.Should().Be(AppleCusip);
    }

    [Fact]
    public async Task IngestRecentFilings_RegistrantIsTrackedStock_SkipsWithoutDownloading()
    {
        var (service, dbContext, secClient) = CreateServiceWithDeps();
        SeedStock(dbContext, "AAPL", cik: "0000320193", cusip: AppleCusip);
        // A standalone ETF trust we track — its CIK appears padded in CommonStock, unpadded in the
        // daily index; normalisation must still match so the issuer feed keeps ownership.
        SeedStock(dbContext, "SPY", cik: "0000884394", cusip: "78462F103");

        var entry = Entry("0000884394-26-000005", cik: "884394");
        StubDailyIndex(secClient, entry);

        var result = await service.IngestRecentFilings(
            new DateOnly(2026, 3, 1),
            lookbackDays: 1,
            maxFetchesPerCycle: 100,
            CancellationToken.None
        );

        result.Stored.Should().Be(0);
        (await dbContext.Set<NportFiling>().AnyAsync()).Should().BeFalse();
        // The submission is never downloaded — the registrant is recognised as a tracked stock first.
        await secClient
            .DidNotReceive()
            .GetDocumentContent(entry.AccessionNumber, Arg.Any<string>());
        (
            await dbContext
                .Set<ProcessedNportFiling>()
                .AnyAsync(p => p.AccessionNumber == entry.AccessionNumber)
        )
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task IngestRecentFilings_TrustHoldingNothingTracked_RecordsSkipDoesNotStore()
    {
        var (service, dbContext, secClient) = CreateServiceWithDeps();
        SeedStock(dbContext, "AAPL", cik: "0000320193", cusip: AppleCusip);

        var entry = Entry("0000999999-26-000007", cik: "999999");
        StubDailyIndex(secClient, entry);
        StubContent(
            secClient,
            entry,
            NportXml("SOME BOND TRUST", "S000099999", (BondCusip, "US TREASURY NOTE", 9_000_000m))
        );

        var result = await service.IngestRecentFilings(
            new DateOnly(2026, 3, 1),
            lookbackDays: 1,
            maxFetchesPerCycle: 100,
            CancellationToken.None
        );

        result.Stored.Should().Be(0);
        (await dbContext.Set<NportFiling>().AnyAsync()).Should().BeFalse();
        (
            await dbContext
                .Set<ProcessedNportFiling>()
                .AnyAsync(p => p.AccessionNumber == entry.AccessionNumber)
        )
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task IngestRecentFilings_AlreadyHandled_NotReDownloadedOnNextCycle()
    {
        var (service, dbContext, secClient) = CreateServiceWithDeps();
        SeedStock(dbContext, "AAPL", cik: "0000320193", cusip: AppleCusip);

        var stored = Entry("0000036405-26-000001", cik: "36405");
        var skipped = Entry("0000999999-26-000007", cik: "999999");
        StubDailyIndex(secClient, stored, skipped);
        StubContent(
            secClient,
            stored,
            NportXml("VANGUARD INDEX FUNDS", "S000002277", (AppleCusip, "APPLE INC", 1m))
        );
        StubContent(
            secClient,
            skipped,
            NportXml("SOME BOND TRUST", "S000099999", (BondCusip, "US TREASURY NOTE", 1m))
        );

        await service.IngestRecentFilings(new DateOnly(2026, 3, 1), 1, 100, CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var second = await service.IngestRecentFilings(
            new DateOnly(2026, 3, 1),
            1,
            100,
            CancellationToken.None
        );

        second.Stored.Should().Be(0);
        // Each submission was fetched exactly once across both cycles — the dedup ledger holds.
        await secClient.Received(1).GetDocumentContent(stored.AccessionNumber, Arg.Any<string>());
        await secClient.Received(1).GetDocumentContent(skipped.AccessionNumber, Arg.Any<string>());
    }

    // ── Helpers ──

    private static (
        NportRealtimeIngestionService service,
        Equibles.Data.EquiblesFinancialDbContext dbContext,
        ISecEdgarClient secClient
    ) CreateServiceWithDeps()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        var secClient = Substitute.For<ISecEdgarClient>();
        var errorManager = new ErrorManager(new ErrorRepository(dbContext));
        var scopeFactory = ServiceScopeSubstitute.Create((typeof(ErrorManager), errorManager));
        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var service = new NportRealtimeIngestionService(
            secClient,
            new CommonStockRepository(dbContext),
            new NportFilingRepository(dbContext),
            new ProcessedNportFilingRepository(dbContext),
            dbContext,
            errorReporter,
            Substitute.For<ILogger<NportRealtimeIngestionService>>()
        );

        return (service, dbContext, secClient);
    }

    private static void SeedStock(
        Equibles.Data.EquiblesFinancialDbContext dbContext,
        string ticker,
        string cik,
        string cusip
    )
    {
        dbContext.Add(
            new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                Name = ticker,
                Cik = cik,
                Cusip = cusip,
            }
        );
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
    }

    private static EdgarDailyIndexEntry Entry(string accession, string cik) =>
        new()
        {
            FormType = "NPORT-P",
            CompanyName = "FUND",
            Cik = cik,
            DateFiled = new DateOnly(2026, 3, 1),
            AccessionNumber = accession,
        };

    private static void StubDailyIndex(
        ISecEdgarClient secClient,
        params EdgarDailyIndexEntry[] entries
    ) =>
        secClient
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(entries.ToList());

    private static void StubContent(
        ISecEdgarClient secClient,
        EdgarDailyIndexEntry entry,
        string content
    ) => secClient.GetDocumentContent(entry.AccessionNumber, entry.Cik).Returns(content);

    private static string NportXml(
        string regName,
        string seriesId,
        params (string cusip, string name, decimal valueUsd)[] holdings
    )
    {
        var rows = string.Join(
            "\n",
            holdings.Select(h =>
                $"""
                  <invstOrSec>
                    <name>{h.name}</name>
                    <cusip>{h.cusip}</cusip>
                    <balance>1.00</balance>
                    <units>NS</units>
                    <curCd>USD</curCd>
                    <valUSD>{h.valueUsd}</valUSD>
                    <pctVal>1.00</pctVal>
                    <payoffProfile>Long</payoffProfile>
                    <assetCat>EC</assetCat>
                    <issuerCat>CORP</issuerCat>
                    <invCountry>US</invCountry>
                  </invstOrSec>
                """
            )
        );

        return $"""
            <SEC-DOCUMENT>
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
                  <regName>{regName}</regName>
                  <seriesName>{regName} Fund</seriesName>
                  <seriesId>{seriesId}</seriesId>
                  <repPdEnd>2026-08-31</repPdEnd>
                  <repPdDate>2026-02-28</repPdDate>
                  <isFinalFiling>N</isFinalFiling>
                </genInfo>
                <fundInfo>
                  <totAssets>1000</totAssets>
                  <totLiabs>0</totLiabs>
                  <netAssets>1000</netAssets>
                </fundInfo>
                <invstOrSecs>
            {rows}
                </invstOrSecs>
              </formData>
            </edgarSubmission>
            </XML>
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;
    }
}

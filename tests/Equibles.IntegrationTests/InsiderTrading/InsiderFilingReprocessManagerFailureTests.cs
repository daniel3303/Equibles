using Equibles.CommonStocks.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Failure-path pin for the reprocess loop. A stale row whose ownership XML can be obtained from
/// neither a cached blob (none here) NOR EDGAR (the client returns null) cannot be re-parsed, so it
/// is recorded as a failure. After MaxCaptureAttempts (3) retries the manager marks the filing
/// NotPresent and stamps the rows to the current version so the run terminates instead of
/// re-selecting the row forever — and it is counted Failed, never Reclassified. The existing test
/// only covers the cache-hit reclassify path; this pins the exhaustion arm.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerFailureTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerFailureTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_StaleRowWithNoCacheAndNoEdgarContent_FailsAndStopsSelectingAfterRetryCeiling()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000099";

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001",
            Name = "Jane Insider",
            City = "Cupertino",
            StateOrCountry = "CA",
            IsDirector = true,
        };
        var stale = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 0,
            FilingDate = date,
            TransactionDate = date,
            TransactionCode = TransactionCode.Purchase,
            Shares = 1000,
            PricePerShare = 55m,
            ReportedPricePerShare = 55m,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.Derivative,
            ParserVersion = 0,
        };

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(stale);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // No cached InsiderFiling exists, and the EDGAR client returns null (default substitute),
        // so neither source yields parseable ownership XML.
        var edgar = Substitute.For<ISecEdgarClient>();
        var fileManager = InsiderReprocessTestSupport.NewFileManager();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            edgar,
            fileManager,
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Reclassified.Should().Be(0);
        result.Repaired.Should().Be(0);
        result.Fetched.Should().Be(0);
        // 3 = MaxCaptureAttempts: each pass re-attempts the still-stale row until the ceiling.
        result.Failed.Should().Be(3);
        await edgar.Received().GetDocumentContent(accession, Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var filing = await verify
            .Set<InsiderFiling>()
            .SingleAsync(f => f.AccessionNumber == accession);
        filing.CaptureStatus.Should().Be(InsiderFilingCaptureStatus.NotPresent);
        var row = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        row!.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

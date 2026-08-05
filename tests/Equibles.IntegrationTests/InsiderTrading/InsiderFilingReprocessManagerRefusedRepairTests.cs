using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// The refusal arm of the reprocess repair path: an implausible price whose share-count
/// division does NOT land inside the session band is flagged invalid and left as filed —
/// never divided and published. This is the exact fixture shape the old validator "repaired"
/// into a fabricated $1,000 price ($1,000,000 over 1,000 shares against a $50 close, 20× the
/// session), persisted end-to-end through the manager.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerRefusedRepairTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerRefusedRepairTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_RepairCandidateOutsideTheBand_FlagsInvalidWithoutFabricating()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000056";

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
            OwnerCik = "0002",
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
            Shares = 1_000,
            PricePerShare = 1_000_000m,
            ReportedPricePerShare = 1_000_000m,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
            ParserVersion = 0,
        };

        var ownershipXml =
            "<ownershipDocument>"
            + "<nonDerivativeTable><nonDerivativeTransaction>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<transactionDate><value>2024-06-14</value></transactionDate>"
            + "<transactionCoding><transactionCode>P</transactionCode></transactionCoding>"
            + "<transactionAmounts>"
            + "<transactionShares><value>1000</value></transactionShares>"
            + "<transactionPricePerShare><value>1000000</value></transactionPricePerShare>"
            + "</transactionAmounts>"
            + "</nonDerivativeTransaction></nonDerivativeTable>"
            + "</ownershipDocument>";
        var rawBytes = Encoding.UTF8.GetBytes(ownershipXml);
        var filing = new InsiderFiling
        {
            AccessionNumber = accession,
            CaptureStatus = InsiderFilingCaptureStatus.Captured,
            UncompressedSize = rawBytes.Length,
            Content = new File
            {
                Name = accession,
                Extension = "gz",
                ContentType = "application/gzip",
                FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
            },
        };

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                Date = date,
                Close = 50m,
            }
        );
        DbContext.Add(stale);
        DbContext.Add(filing);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var edgar = Substitute.For<ISecEdgarClient>();
        var fileManager = InsiderReprocessTestSupport.NewFileManager();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderFilingReprocessManager(
            new InsiderTransactionRepository(runCtx),
            new InsiderFilingRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new StockSplitRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            edgar,
            fileManager,
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Repaired.Should().Be(0, "a candidate 20x the session band is not a repair");
        result.Failed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var row = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        row!.PricePerShare.Should().Be(1_000_000m, "a refused repair must not fabricate a price");
        row.ReportedPricePerShare.Should().Be(1_000_000m);
        row.PriceWasRepaired.Should().BeFalse();
        row.IsPriceValid.Should().Be(false);
        row.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

using System.Text;
using Equibles.CommonStocks.Data.Models;
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
/// Price-repair pin for the reprocess loop. A row whose reported per-share price is implausible
/// against the close (a total-value figure mis-entered in the price field) is repaired by dividing
/// by the share count, and the run counts it as Repaired. The existing tests (reclassify, fetch,
/// failure) never exercise the Repaired arm — they all leave Repaired=0.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerRepairTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerRepairTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_ImplausiblePriceAgainstClose_RepairsByDividingByShares()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000055";

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
        // Already NonDerivative (so no reclassify), but the reported price is a total-value figure
        // (1,000,000) — ~20000x the $50 close — so the validator must repair it to 1,000,000 / 1000.
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
        var fileManager = Substitute.For<IFileManager>();

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

        result.Repaired.Should().Be(1);
        result.Reclassified.Should().Be(0);
        result.Failed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var row = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        // Repaired to 1,000,000 / 1000 shares = 1,000 per share; the as-filed value is preserved.
        row!.PricePerShare.Should().Be(1_000m);
        row.ReportedPricePerShare.Should().Be(1_000_000m);
        row.IsPriceValid.Should().Be(true);
        row.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

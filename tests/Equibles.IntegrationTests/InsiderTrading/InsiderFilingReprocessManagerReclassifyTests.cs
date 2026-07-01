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
/// End-to-end pin for the version-driven reprocess loop, which needs a real
/// relational provider (it calls Database.SetCommandTimeout, unsupported by the
/// in-memory provider). Contract: a row below the current parser version is
/// re-parsed from its cached ownership XML — a local read, not an EDGAR fetch —
/// SecurityKind is re-derived from the source table (authoritative over the
/// stored value), and the row is stamped with the current parser version so it
/// drops out of future runs.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerReclassifyTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerReclassifyTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_StaleRowWithCachedXml_ReclassifiesFromSourceTableAndStampsVersion()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000001";

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

        // Stored row is mis-classified as Derivative and stuck at the legacy
        // version; the cached XML below places it in the non-derivative table, so
        // the run must flip it to NonDerivative — the table, never the prior value,
        // is authoritative.
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

        // The reprocess maps a re-parsed row onto the stored row by TransactionOrder,
        // so the cached XML's single non-derivative transaction (order 0) lines up.
        var ownershipXml =
            "<ownershipDocument>"
            + "<nonDerivativeTable><nonDerivativeTransaction>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<transactionDate><value>2024-06-14</value></transactionDate>"
            + "<transactionCoding><transactionCode>P</transactionCode></transactionCoding>"
            + "<transactionAmounts>"
            + "<transactionShares><value>1000</value></transactionShares>"
            + "<transactionPricePerShare><value>55</value></transactionPricePerShare>"
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
                Close = 55m,
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
            new InsiderTransactionPriceValidator(),
            edgar,
            fileManager,
            runCtx,
            NullLogger<InsiderFilingReprocessManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.Reclassified.Should().Be(1);
        result.Repaired.Should().Be(0);
        result.Failed.Should().Be(0);
        // Served entirely from the cached blob — no EDGAR round-trip.
        result.Fetched.Should().Be(0);
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var reprocessed = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        reprocessed!.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        reprocessed.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

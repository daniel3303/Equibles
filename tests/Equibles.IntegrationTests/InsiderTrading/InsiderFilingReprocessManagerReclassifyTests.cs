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
/// End-to-end pin for the version-driven reprocess loop, which needs a real
/// relational provider (it calls Database.SetCommandTimeout, unsupported by the
/// in-memory provider). Contract: a row below the current parser version is
/// re-parsed from its cached ownership XML — a local read, not an EDGAR fetch —
/// SecurityKind and corrected dates are re-derived from the source (authoritative
/// over stored values), and the row is stamped with the current parser version so
/// it drops out of future runs.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerReclassifyTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerReclassifyTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_V5RowWithCachedXml_ReclassifiesAndCorrectsDateFromSource()
    {
        var reportDate = new DateOnly(2024, 6, 14);
        var filingDate = new DateOnly(2024, 6, 17);
        var impossibleDate = new DateOnly(2035, 6, 14);
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

        // This v5 row predates the parser generation for date correction. Its source
        // date is impossible and its security type is wrong; cached XML authoritatively
        // supplies both the table classification and the period-of-report fallback.
        var stale = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = accession,
            TransactionOrder = 0,
            FilingDate = filingDate,
            TransactionDate = impossibleDate,
            TransactionCode = TransactionCode.Purchase,
            Shares = 1000,
            PricePerShare = 55m,
            ReportedPricePerShare = 55m,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.Derivative,
            ParserVersion = 5,
        };

        // The reprocess maps a re-parsed row onto the stored row by TransactionOrder,
        // so the cached XML's single non-derivative transaction (order 0) lines up.
        var ownershipXml =
            "<ownershipDocument>"
            + "<periodOfReport>2024-06-14</periodOfReport>"
            + "<nonDerivativeTable><nonDerivativeTransaction>"
            + "<securityTitle><value>Common Stock</value></securityTitle>"
            + "<transactionDate><value>2035-06-14</value></transactionDate>"
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
                Date = reportDate,
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
            new StockSplitRepository(runCtx),
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
        result.DatesCorrected.Should().Be(1);
        result.Failed.Should().Be(0);
        // Served entirely from the cached blob — no EDGAR round-trip.
        result.Fetched.Should().Be(0);
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var reprocessed = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        reprocessed!.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        reprocessed.TransactionDate.Should().Be(reportDate);
        reprocessed.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

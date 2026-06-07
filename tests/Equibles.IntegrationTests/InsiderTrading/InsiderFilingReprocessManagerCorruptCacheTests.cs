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
/// Pin for the corrupt-cache fall-through in <c>GetOwnershipRoot</c>. A filing marked
/// <c>Captured</c> whose stored blob decompresses to something that is not a parseable
/// ownership document must not be returned as null forever (which would re-select the
/// filing on every run) — it must fall through to a fresh EDGAR re-fetch, re-cache, and
/// reprocess the rows.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerCorruptCacheTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerCorruptCacheTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_CapturedFilingWithUnparseableCachedBlob_RefetchesFromEdgarAndReprocesses()
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

        // Marked Captured, but the stored blob is valid gzip that decompresses to
        // non-XML — TryGetOwnershipRoot returns null, so the cache hit must be abandoned.
        var corruptBytes = Encoding.UTF8.GetBytes("this is not an ownership document");
        var filing = new InsiderFiling
        {
            AccessionNumber = accession,
            CaptureStatus = InsiderFilingCaptureStatus.Captured,
            UncompressedSize = corruptBytes.Length,
            Content = new File
            {
                Name = accession,
                Extension = "gz",
                ContentType = "application/gzip",
                FileContent = new FileContent { Bytes = GzipCompressor.Compress(corruptBytes) },
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

        // The re-fetch returns a valid non-derivative document for order 0.
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
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetDocumentContent(Arg.Any<string>(), Arg.Any<string>()).Returns(ownershipXml);

        await using var runCtx = Fixture.CreateDbContext();

        // Mirror the real file manager: it tracks the new File as Added on the same
        // context the manager saves, so the re-cache write satisfies the ContentId FK.
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .SaveInternalFile(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(ci =>
            {
                var file = new File
                {
                    Name = ci.ArgAt<string>(1),
                    Extension = "gz",
                    ContentType = "application/gzip",
                    FileContent = new FileContent { Bytes = ci.ArgAt<byte[]>(0) },
                };
                runCtx.Add(file);
                return file;
            });

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

        // Corrupt cache must not strand the filing: it re-fetches from EDGAR and reprocesses.
        result.Fetched.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Processed.Should().Be(1);
        result.Reclassified.Should().Be(1);
        await edgar.Received(1).GetDocumentContent(accession, stock.Cik);

        await using var verify = Fixture.CreateDbContext();
        var reprocessed = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        reprocessed!.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        reprocessed.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

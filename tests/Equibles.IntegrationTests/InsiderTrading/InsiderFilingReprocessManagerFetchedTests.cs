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

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// EDGAR-fetch pin for the reprocess loop. A stale row with NO cached blob must re-fetch the
/// ownership XML from EDGAR (counted as a Fetched round-trip), cache it via the file manager, and
/// reprocess off the fetched document — re-deriving SecurityKind from the source table. The
/// existing tests cover only the cache-hit reclassify (Fetched=0) and the no-content failure
/// (Fetched=0) paths; neither exercises the successful EDGAR round-trip + cache-write arm.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerFetchedTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerFetchedTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_StaleRowNoCacheButEdgarReturnsXml_FetchesCachesAndReclassifies()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000077";

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
        // Stored as Derivative at the legacy version; the fetched XML places it in the
        // non-derivative table, so the run must flip it — the table is authoritative.
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
        DbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                Date = date,
                Close = 55m,
            }
        );
        DbContext.Add(stale);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

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

        // No cached InsiderFiling exists, so the manager must reach EDGAR; the client supplies the
        // ownership XML and the file manager stands in for the cache write.
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetDocumentContent(accession, Arg.Any<string>()).Returns(ownershipXml);
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .SaveInternalFile(
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            )
            .Returns(ci => new File
            {
                Name = accession,
                Extension = "gz",
                ContentType = "application/gzip",
            });

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

        result.Fetched.Should().Be(1);
        result.Reclassified.Should().Be(1);
        result.Failed.Should().Be(0);
        await edgar.Received().GetDocumentContent(accession, Arg.Any<string>());
        await fileManager
            .Received()
            .SaveInternalFile(Arg.Any<byte[]>(), accession, Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var row = await verify.Set<InsiderTransaction>().FindAsync(stale.Id);
        row!.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        row.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}

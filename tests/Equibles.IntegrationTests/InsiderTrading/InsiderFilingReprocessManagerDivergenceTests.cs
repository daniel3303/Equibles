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
/// Pin for the row-count divergence path in <c>ReprocessFiling</c>. When the cached XML
/// re-parses to fewer transactions than the stored filing has rows, each stored row is
/// matched to a parsed row by <c>TransactionOrder</c>; a stored row with no match keeps
/// its prior <c>SecurityKind</c>/<c>Notes</c> but must still be advanced to the current
/// parser version, otherwise it would be re-selected on every future run.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessManagerDivergenceTests : ParadeDbMcpTestBase
{
    public InsiderFilingReprocessManagerDivergenceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_CachedXmlReparsesFewerRowsThanStored_AdvancesUnmatchedRowKeepingPriorData()
    {
        var date = new DateOnly(2024, 6, 14);
        var accession = "0000320193-24-000050";

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

        // Two stale rows. The cached XML below has a single transaction (order 0), so
        // order 0 re-parses and reclassifies while order 1 has no parsed counterpart.
        InsiderTransaction MakeStale(int order) =>
            new()
            {
                Id = Guid.NewGuid(),
                CommonStockId = stock.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = accession,
                TransactionOrder = order,
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
        var matched = MakeStale(0);
        var unmatched = MakeStale(1);

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
        DbContext.Add(matched);
        DbContext.Add(unmatched);
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

        result.Failed.Should().Be(0);
        result.Processed.Should().Be(1);
        // Only the matched row flipped Derivative -> NonDerivative.
        result.Reclassified.Should().Be(1);
        // Served from the cache, no EDGAR round-trip.
        await edgar.DidNotReceive().GetDocumentContent(Arg.Any<string>(), Arg.Any<string>());

        await using var verify = Fixture.CreateDbContext();
        var matchedAfter = await verify.Set<InsiderTransaction>().FindAsync(matched.Id);
        var unmatchedAfter = await verify.Set<InsiderTransaction>().FindAsync(unmatched.Id);

        matchedAfter!.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        matchedAfter.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);

        // The unmatched row keeps its prior kind but must still advance, or it would be
        // re-selected forever.
        unmatchedAfter!.SecurityKind.Should().Be(InsiderSecurityKind.Derivative);
        unmatchedAfter.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
    }
}
